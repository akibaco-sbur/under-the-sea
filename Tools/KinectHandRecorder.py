"""Record a small synchronized Azure Kinect RGB/depth hand benchmark take.

No image leaves the local computer. The guided labels are prompts, not verified
ground truth: review the recording before using the stable intervals for scoring.
"""
import argparse
import csv
import ctypes
import datetime
import json
import os
from pathlib import Path
import queue
import tempfile
import threading
import time


ROOT = Path(__file__).resolve().parent.parent
RUNTIME = ROOT / '.Local' / 'AzureKinect' / 'Runtime'
OUTPUT = ROOT / '.Local' / 'HandBenchmark'
PHASES = [
    ('OPEN', '右手を前に出し、パーのまま止める', 'front'),
    ('CLOSED', '右手を前に出し、グーのまま止める', 'front'),
    ('OPEN', '右手を胸の前に重ね、パーのまま止める', 'body_overlap'),
    ('CLOSED', '右手を胸の前に重ね、グーのまま止める', 'body_overlap'),
    ('OPEN', '右手をパーのまま、ゆっくり回す', 'rotate'),
    ('CLOSED', '右手をグーのまま、ゆっくり回す', 'rotate'),
    ('OPEN', '右手をパーにして、左手で半分隠す', 'partial_occlusion'),
    ('CLOSED', '右手をグーにして、左手で半分隠す', 'partial_occlusion'),
]
FIST_DISTANCE_PHASES = [
    ('CLOSED', '右手を体の右外側へ伸ばし\nグーのまま止める', 'outside_still'),
    ('CLOSED', '右手を体の右外側へ伸ばし\nグーのままゆっくり回す', 'outside_rotate'),
    ('CLOSED', '右手を胸の中央の前へ伸ばし\nグーのまま止める', 'chest_camera_still'),
    ('CLOSED', '右手を胸の中央の前へ伸ばし\nグーのままゆっくり回す', 'chest_camera_rotate'),
    ('CLOSED', '右手を胸の中央の前へ出し\n肘を軽く曲げてグーのまま止める', 'chest_middle_still'),
    ('CLOSED', '右手を胸の中央の前へ出し\n肘を軽く曲げてグーのまま回す', 'chest_middle_rotate'),
    ('CLOSED', '右手を胸の中央のすぐ前に置き\nグーのまま止める', 'chest_near_still'),
    ('CLOSED', '右手を胸の中央のすぐ前に置き\nグーのままゆっくり回す', 'chest_near_rotate'),
]
PREPARE_SECONDS = 10.0
PROBE_SECONDS = 5.0


class DeviceConfiguration(ctypes.Structure):
    _fields_ = [
        ('colorFormat', ctypes.c_int), ('colorResolution', ctypes.c_int),
        ('depthMode', ctypes.c_int), ('cameraFps', ctypes.c_int),
        ('synchronizedImagesOnly', ctypes.c_bool), ('depthDelay', ctypes.c_int),
        ('wiredSyncMode', ctypes.c_int), ('subordinateDelay', ctypes.c_uint32),
        ('disableStreamingIndicator', ctypes.c_bool),
    ]


def bind(dll, name, argtypes, restype):
    function = getattr(dll, name)
    function.argtypes = argtypes
    function.restype = restype
    return function


class KinectApi:
    def __init__(self):
        if os.name != 'nt' or ctypes.sizeof(ctypes.c_void_p) != 8 or ctypes.sizeof(DeviceConfiguration) != 36:
            raise RuntimeError('This recorder requires Windows x64 and the expected Kinect SDK ABI.')
        self.dllDirectory = os.add_dll_directory(str(RUNTIME))
        self.sensor = ctypes.CDLL(str(RUNTIME / 'k4a.dll'))
        self.record = ctypes.CDLL(str(RUNTIME / 'k4arecord.dll'))
        pointer = ctypes.c_void_p
        result = ctypes.c_int
        bind(self.sensor, 'k4a_device_get_installed_count', [], ctypes.c_uint32)
        bind(self.sensor, 'k4a_device_open', [ctypes.c_uint32, ctypes.POINTER(pointer)], result)
        bind(self.sensor, 'k4a_device_close', [pointer], None)
        bind(self.sensor, 'k4a_device_start_cameras', [pointer, ctypes.POINTER(DeviceConfiguration)], result)
        bind(self.sensor, 'k4a_device_stop_cameras', [pointer], None)
        bind(self.sensor, 'k4a_device_get_capture', [pointer, ctypes.POINTER(pointer), ctypes.c_int], result)
        bind(self.sensor, 'k4a_capture_get_color_image', [pointer], pointer)
        bind(self.sensor, 'k4a_capture_get_depth_image', [pointer], pointer)
        bind(self.sensor, 'k4a_capture_release', [pointer], None)
        bind(self.sensor, 'k4a_image_release', [pointer], None)
        bind(self.sensor, 'k4a_image_get_device_timestamp_usec', [pointer], ctypes.c_uint64)
        bind(self.sensor, 'k4a_image_get_width_pixels', [pointer], ctypes.c_int)
        bind(self.sensor, 'k4a_image_get_height_pixels', [pointer], ctypes.c_int)
        bind(self.record, 'k4a_record_create', [ctypes.c_char_p, pointer, DeviceConfiguration, ctypes.POINTER(pointer)], result)
        bind(self.record, 'k4a_record_write_header', [pointer], result)
        bind(self.record, 'k4a_record_write_capture', [pointer, pointer], result)
        bind(self.record, 'k4a_record_flush', [pointer], result)
        bind(self.record, 'k4a_record_close', [pointer], None)
        bind(self.record, 'k4a_playback_open', [ctypes.c_char_p, ctypes.POINTER(pointer)], result)
        bind(self.record, 'k4a_playback_get_calibration', [pointer, pointer], result)
        bind(self.record, 'k4a_playback_get_next_capture', [pointer, ctypes.POINTER(pointer)], result)
        bind(self.record, 'k4a_playback_close', [pointer], None)

    @staticmethod
    def check(result, operation):
        if result != 0:
            raise RuntimeError(f'{operation} failed (SDK result {result}).')

    def imageInfo(self, capture):
        color = self.sensor.k4a_capture_get_color_image(capture)
        depth = self.sensor.k4a_capture_get_depth_image(capture)
        try:
            if not color or not depth:
                return None
            return {
                'colorTimestampUs': self.sensor.k4a_image_get_device_timestamp_usec(color),
                'depthTimestampUs': self.sensor.k4a_image_get_device_timestamp_usec(depth),
                'colorSize': [self.sensor.k4a_image_get_width_pixels(color), self.sensor.k4a_image_get_height_pixels(color)],
                'depthSize': [self.sensor.k4a_image_get_width_pixels(depth), self.sensor.k4a_image_get_height_pixels(depth)],
            }
        finally:
            if color:
                self.sensor.k4a_image_release(color)
            if depth:
                self.sensor.k4a_image_release(depth)

    def validate(self, path, rows):
        playback = ctypes.c_void_p()
        self.check(self.record.k4a_playback_open(os.fsencode(path), ctypes.byref(playback)), 'Open recorded file')
        found = []
        try:
            calibration = ctypes.create_string_buffer(1032)
            self.check(self.record.k4a_playback_get_calibration(playback, calibration), 'Read saved calibration')
            while True:
                capture = ctypes.c_void_p()
                state = self.record.k4a_playback_get_next_capture(playback, ctypes.byref(capture))
                if state == 2:  # K4A_STREAM_RESULT_EOF
                    break
                self.check(state, 'Read recorded frame')
                try:
                    info = self.imageInfo(capture)
                    if info is None:
                        raise RuntimeError('A recorded capture is missing color or depth.')
                    found.append(info)
                finally:
                    self.sensor.k4a_capture_release(capture)
        finally:
            self.record.k4a_playback_close(playback)
        if not found or len(found) != len(rows):
            raise RuntimeError(f'Playback has {len(found)} paired frames; recording reported {len(rows)}.')
        for index, (actual, expected) in enumerate(zip(found, rows)):
            if (actual['colorTimestampUs'] != expected['colorTimestampUs'] or
                    actual['depthTimestampUs'] != expected['depthTimestampUs'] or
                    actual['colorSize'] != [1280, 720] or actual['depthSize'] != [640, 576]):
                raise RuntimeError(f'Playback mismatch at frame {index}.')
        return len(found)


def phaseAt(elapsed, phases, prepareSeconds=PREPARE_SECONDS, probeSeconds=PROBE_SECONDS):
    if elapsed < 0 or elapsed >= len(phases) * (prepareSeconds + probeSeconds):
        return None
    phaseDuration = prepareSeconds + probeSeconds
    index = int(elapsed // phaseDuration)
    phaseTime = elapsed - index * phaseDuration
    label, text, condition = phases[index]
    stage = 'PREPARE' if phaseTime < prepareSeconds else 'PROBE'
    stageTime = phaseTime if stage == 'PREPARE' else phaseTime - prepareSeconds
    stageDuration = prepareSeconds if stage == 'PREPARE' else probeSeconds
    return index, label, text, condition, stage, stageTime, stageDuration


def recordTake(api, phases, startEvent, stopEvent, onReady, onStarted=lambda _: None,
               prepareSeconds=PREPARE_SECONDS, probeSeconds=PROBE_SECONDS):
    OUTPUT.mkdir(parents=True, exist_ok=True)
    prefix = 'take-' + datetime.datetime.now().strftime('%Y%m%d-%H%M%S') + '-'
    folder = Path(tempfile.mkdtemp(prefix=prefix, dir=OUTPUT))
    path = folder / 'Kinect.mkv'
    device = ctypes.c_void_p()
    recording = ctypes.c_void_p()
    started = False
    rows = []
    try:
        if api.sensor.k4a_device_get_installed_count() == 0:
            raise RuntimeError('Azure Kinect が見つかりません。電源と USB を確認してください。')
        api.check(api.sensor.k4a_device_open(0, ctypes.byref(device)), 'Open Kinect (close other camera users)')
        # Native MJPG 720p, NFOV unbinned depth 640x576, 15fps, paired captures.
        config = DeviceConfiguration(0, 1, 2, 1, True, 0, 0, 0, False)
        api.check(api.sensor.k4a_device_start_cameras(device, ctypes.byref(config)), 'Start RGB and depth cameras')
        started = True
        warmup = ctypes.c_void_p()
        api.check(api.sensor.k4a_device_get_capture(device, ctypes.byref(warmup), 10000), 'Get first paired capture')
        api.sensor.k4a_capture_release(warmup)
        api.check(api.record.k4a_record_create(os.fsencode(path), device, config, ctypes.byref(recording)), 'Create recording')
        api.check(api.record.k4a_record_write_header(recording), 'Write recording header')
        onReady()
        while not startEvent.wait(0.1):
            if stopEvent.is_set():
                raise RuntimeError('Recording cancelled before start.')
        # Discard frames queued during the on-screen countdown. A queued image
        # must not inherit an instruction displayed after that image was captured.
        for _ in range(100):
            queued = ctypes.c_void_p()
            state = api.sensor.k4a_device_get_capture(device, ctypes.byref(queued), 0)
            if state == 2:
                break
            api.check(state, 'Drain countdown frames')
            api.sensor.k4a_capture_release(queued)
        startTime = time.monotonic()
        onStarted(startTime)
        total = len(phases) * (prepareSeconds + probeSeconds)
        previousColor = previousDepth = -1
        while not stopEvent.is_set():
            elapsed = time.monotonic() - startTime
            if elapsed >= total:
                break
            capture = ctypes.c_void_p()
            state = api.sensor.k4a_device_get_capture(device, ctypes.byref(capture), 1000)
            if state == 2:  # K4A_WAIT_RESULT_TIMEOUT
                continue
            api.check(state, 'Read capture')
            try:
                elapsed = time.monotonic() - startTime
                phase = phaseAt(elapsed, phases, prepareSeconds, probeSeconds)
                if phase is None:
                    break
                info = api.imageInfo(capture)
                if info is None:
                    continue
                if info['colorTimestampUs'] <= previousColor or info['depthTimestampUs'] <= previousDepth:
                    raise RuntimeError('Camera timestamps did not increase.')
                api.check(api.record.k4a_record_write_capture(recording, capture), 'Write paired capture')
                previousColor, previousDepth = info['colorTimestampUs'], info['depthTimestampUs']
                index, label, _, condition, stage, _, _ = phase
                rows.append({
                    'frame': len(rows), 'colorTimestampUs': previousColor, 'depthTimestampUs': previousDepth,
                    'hostElapsedSeconds': round(elapsed, 6), 'promptIndex': index,
                    'condition': condition, 'stage': stage,
                    'suggestedLabel': label if stage == 'PROBE' else '', 'transition': stage != 'PROBE',
                    'verifiedLabel': '',
                })
            finally:
                api.sensor.k4a_capture_release(capture)
        if stopEvent.is_set():
            raise RuntimeError('Recording cancelled before completion.')
        api.check(api.record.k4a_record_flush(recording), 'Flush recording')
        api.record.k4a_record_close(recording)
        recording = ctypes.c_void_p()
        api.sensor.k4a_device_stop_cameras(device)
        started = False
        api.sensor.k4a_device_close(device)
        device = ctypes.c_void_p()
        frameCount = api.validate(path, rows)
        with (folder / 'Frames.csv').open('x', newline='', encoding='utf-8') as file:
            writer = csv.DictWriter(file, fieldnames=list(rows[0]))
            writer.writeheader()
            writer.writerows(rows)
        metadata = {
            'schema': 'under-the-sea.kinect-hand-recording.v2',
            'recording': path.name, 'frames': frameCount,
            'color': '720p MJPG', 'depth': 'NFOV_UNBINNED', 'fpsRequested': 15,
            'prepareSeconds': prepareSeconds, 'probeSeconds': probeSeconds,
            'labels': 'Suggested prompts only; verifiedLabel must be reviewed before scoring.',
            'phases': [{'suggestedLabel': label, 'instruction': text, 'condition': condition} for label, text, condition in phases],
        }
        with (folder / 'Session.json').open('x', encoding='utf-8') as file:
            json.dump(metadata, file, ensure_ascii=False, indent=2)
        return folder, frameCount
    except Exception as error:
        # An incomplete take has no Session.json and is never called valid.
        try:
            with (folder / 'Invalid.json').open('x', encoding='utf-8') as file:
                json.dump({'error': str(error), 'framesWritten': len(rows)}, file, ensure_ascii=False, indent=2)
        except OSError:
            pass
        raise
    finally:
        if recording:
            api.record.k4a_record_close(recording)
        if started:
            api.sensor.k4a_device_stop_cameras(device)
        if device:
            api.sensor.k4a_device_close(device)


def smoke():
    api = KinectApi()
    start = threading.Event()
    start.set()
    folder, count = recordTake(api, [('SMOKE', 'No participant required', 'smoke')], start, threading.Event(), lambda: None, prepareSeconds=1.0, probeSeconds=2.0)
    print(json.dumps({'result': 'ok', 'path': str(folder), 'frames': count}, ensure_ascii=False), flush=True)


def guided(phases=PHASES):
    import tkinter as tk

    root = tk.Tk()
    root.title('Kinect 手の録画')
    root.geometry('920x500')
    root.configure(bg='#14202a')
    root.attributes('-topmost', True)
    message = tk.StringVar(value='右手の開閉を録画します\n準備ができたら、下のボタンを押してください')
    detail = tk.StringVar(value=f'約{len(phases) * (PREPARE_SECONDS + PROBE_SECONDS) / 60:g}分・各動作は準備10秒、計測5秒です')
    tk.Label(root, textvariable=message, bg='#14202a', fg='white', font=('Yu Gothic UI', 30, 'bold'), wraplength=850, justify='center').pack(expand=True, fill='both', padx=25, pady=20)
    tk.Label(root, textvariable=detail, bg='#14202a', fg='#83dce8', font=('Yu Gothic UI', 17), wraplength=850).pack(pady=12)
    startEvent = threading.Event()
    stopEvent = threading.Event()
    events = queue.Queue()
    state = {'started': False, 'zero': None, 'recordingZero': None, 'finished': False, 'closing': False, 'worker': None, 'stage': None}

    def reportReady():
        events.put(('ready', None))

    def run():
        try:
            result = recordTake(KinectApi(), phases, startEvent, stopEvent, reportReady,
                                lambda started: events.put(('recordingStarted', started)))
            events.put(('done', result))
        except Exception as error:
            events.put(('error', str(error)))

    def begin():
        if state['started']:
            return
        state['started'] = True
        button.configure(state='disabled')
        message.set('カメラを準備しています…')
        detail.set('人物検出は不要です。少しお待ちください。')
        state['worker'] = threading.Thread(target=run, daemon=True)
        state['worker'].start()

    button = tk.Button(root, text='録画開始', command=begin, font=('Yu Gothic UI', 21, 'bold'), width=16, height=2)
    button.pack(pady=25)

    def tick():
        try:
            while True:
                kind, value = events.get_nowait()
                if kind == 'ready':
                    state['zero'] = time.monotonic() + 3.0
                elif kind == 'recordingStarted':
                    state['recordingZero'] = value
                elif kind == 'done':
                    folder, count = value
                    state['finished'] = True
                    message.set('録画と再生確認が完了しました')
                    detail.set(f'{count} フレームを保存しました\n{folder}')
                    button.configure(text='閉じる', state='normal', command=root.destroy)
                elif kind == 'error':
                    state['finished'] = True
                    message.set('録画を完了できませんでした')
                    detail.set(value)
                    button.configure(text='閉じる', state='normal', command=root.destroy)
        except queue.Empty:
            pass
        zero = state['zero']
        if zero is not None and not state['finished'] and not state['closing']:
            elapsed = time.monotonic() - zero
            if elapsed < 0:
                message.set(f'もうすぐ録画します\n{max(1, int(-elapsed) + 1)}')
                detail.set('最初の指示を表示するまでお待ちください')
            elif state['recordingZero'] is None:
                if not startEvent.is_set():
                    startEvent.set()
                message.set('録画の準備をしています…')
                detail.set('最初の指示が表示されるまでお待ちください')
            else:
                phase = phaseAt(time.monotonic() - state['recordingZero'], phases)
                if phase is None:
                    message.set('録画が終わりました\n保存と再生確認をしています…')
                    detail.set('カメラの前を離れて大丈夫です')
                else:
                    index, _, text, _, stage, stageTime, duration = phase
                    if state['stage'] != (index, stage):
                        state['stage'] = (index, stage)
                        root.bell()
                    title = '準備：指示を読んで姿勢を作る' if stage == 'PREPARE' else '計測中：その動作を続ける'
                    message.set(f'{title}\n\n{text}')
                    detail.set(f'{index + 1} / {len(phases)}     {"計測まで" if stage == "PREPARE" else "計測終了まで"} {max(1, int(duration - stageTime) + 1)} 秒')
        root.after(50, tick)

    def close():
        if state['worker'] is None or not state['worker'].is_alive():
            root.destroy()
            return
        stopEvent.set()
        startEvent.set()
        state['closing'] = True
        message.set('録画を停止しています…')
        detail.set('ファイルを閉じるまでお待ちください')
        button.configure(state='disabled')
        def waitForWorker():
            if state['worker'].is_alive():
                root.after(100, waitForWorker)
            else:
                root.destroy()
        root.after(100, waitForWorker)

    root.protocol('WM_DELETE_WINDOW', close)
    root.after(50, tick)
    root.mainloop()


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--smoke', action='store_true', help='Record and reopen two seconds without a participant')
    parser.add_argument('--fist-distance', action='store_true', help='Compare fist rotation and distance from the chest')
    args = parser.parse_args()
    if args.smoke:
        smoke()
    else:
        guided(FIST_DISTANCE_PHASES if args.fist_distance else PHASES)
