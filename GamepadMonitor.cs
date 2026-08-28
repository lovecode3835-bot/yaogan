using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace FightstickLab
{
    public sealed class GamepadSnapshot
    {
        public bool Connected { get; set; }
        public int Slot { get; set; } = -1;
        public string? Source { get; set; }   // XInput / DirectInput
        public string? Note { get; set; }
        public bool Up { get; set; }
        public bool Down { get; set; }
        public bool Left { get; set; }
        public bool Right { get; set; }
        public bool LightPunch { get; set; }
        public bool HeavyPunch { get; set; }
        public bool LightKick { get; set; }
        public bool HeavyKick { get; set; }
    }

    public sealed class GamepadMonitor : IDisposable
    {
        private const ushort DpadUp = 0x0001;
        private const ushort DpadDown = 0x0002;
        private const ushort DpadLeft = 0x0004;
        private const ushort DpadRight = 0x0008;
        private const ushort ButtonA = 0x1000;
        private const ushort ButtonB = 0x2000;
        private const ushort ButtonX = 0x4000;
        private const ushort ButtonY = 0x8000;
        private const uint ErrorDeviceNotConnected = 1167;

        private const uint JoyErrNoError = 0;
        private const uint JoyErrUnplugged = 167;
        private const uint MmsysErrBadDeviceId = 2;
        private const uint JoyReturnAll = 0x000000FF;   // X | Y | Z | R | U | V | POV | BUTTONS
        private const uint JoyUndefinedPov = 0xFFFF;
        private const uint AxisCenter = 32768;

        private Timer? _timer;
        private GamepadSnapshot _previous = new GamepadSnapshot();
        public double Deadzone { get; set; } = 0.42;
        public event Action<GamepadSnapshot>? StateChanged;

        // 在同一台机器上来回尝试 xinput1_4 (Win8+) / xinput1_3 (Win7)，找到可用的那个
        private static bool _use14 = true;
        private static bool _xinputUnavailable;

        public void Start() => _timer = new Timer(Poll, null, 0, 16);

        private void Poll(object? state)
        {
            var next = new GamepadSnapshot();

            // 1) 优先 XInput（按钮映射准确）
            if (!_xinputUnavailable)
            {
                XInputSnapshot xinput = PollXInput();
                if (xinput.Connected)
                {
                    next.Connected = true;
                    next.Slot = xinput.Slot;
                    next.Source = "XInput";
                    next.Up = xinput.Up;
                    next.Down = xinput.Down;
                    next.Left = xinput.Left;
                    next.Right = xinput.Right;
                    next.LightPunch = xinput.LightPunch;
                    next.HeavyPunch = xinput.HeavyPunch;
                    next.LightKick = xinput.LightKick;
                    next.HeavyKick = xinput.HeavyKick;
                    EmitIfChanged(next);
                    return;
                }
            }

            // 2) 回退：directinput/HID 摇杆（winmm 的 joyGetPosEx）
            JoySnapshot joy = PollJoystick();
            if (joy.Connected)
            {
                next.Connected = true;
                next.Slot = -1;
                next.Source = "DirectInput";
                next.Up = joy.Up;
                next.Down = joy.Down;
                next.Left = joy.Left;
                next.Right = joy.Right;
                next.LightPunch = joy.LightPunch;
                next.HeavyPunch = joy.HeavyPunch;
                next.LightKick = joy.LightKick;
                next.HeavyKick = joy.HeavyKick;
                EmitIfChanged(next);
                return;
            }

            // 3) 未连接
            next.Connected = false;
            next.Source = _xinputUnavailable ? "DirectInput" : "XInput";
            next.Note = _xinputUnavailable ? "本机没有 XInput 组件" : null;
            EmitIfChanged(next);
        }

        private void EmitIfChanged(GamepadSnapshot next)
        {
            var changed = next.Connected != _previous.Connected
                || next.Slot != _previous.Slot
                || (next.Source ?? string.Empty) != (_previous.Source ?? string.Empty)
                || (next.Note ?? string.Empty) != (_previous.Note ?? string.Empty)
                || !Same(next, _previous);
            if (changed)
            {
                _previous = next;
                StateChanged?.Invoke(next);
            }
        }

        private XInputSnapshot PollXInput()
        {
            var snapshot = new XInputSnapshot();
            for (uint i = 0; i < 4; i++)
            {
                XInputState input;
                uint result;
                try
                {
                    result = GetState(i, out input);
                }
                catch (DllNotFoundException)
                {
                    _xinputUnavailable = true;
                    return snapshot;
                }

                if (result != 0) continue; // 未连接或出错
                snapshot.Connected = true;
                snapshot.Slot = (int)i;
                var threshold = short.MaxValue * Deadzone;
                var buttons = input.Gamepad.Buttons;
                snapshot.Up |= (buttons & DpadUp) != 0 || input.Gamepad.ThumbLY > threshold;
                snapshot.Down |= (buttons & DpadDown) != 0 || input.Gamepad.ThumbLY < -threshold;
                snapshot.Left |= (buttons & DpadLeft) != 0 || input.Gamepad.ThumbLX < -threshold;
                snapshot.Right |= (buttons & DpadRight) != 0 || input.Gamepad.ThumbLX > threshold;
                snapshot.LightPunch |= (buttons & ButtonX) != 0;
                snapshot.HeavyPunch |= (buttons & ButtonY) != 0;
                snapshot.LightKick |= (buttons & ButtonA) != 0;
                snapshot.HeavyKick |= (buttons & ButtonB) != 0;
            }
            return snapshot;
        }

        private JoySnapshot PollJoystick()
        {
            var snapshot = new JoySnapshot();
            for (uint id = 0; id < 16; id++)
            {
                var info = new JOYINFOEX { dwSize = (uint)Marshal.SizeOf(typeof(JOYINFOEX)), dwFlags = JoyReturnAll };
                var result = joyGetPosEx(id, ref info);
                if (result == JoyErrNoError)
                {
                    snapshot.Connected = true;
                    MergeJoystick(snapshot, info);
                    return snapshot;
                }
                if (result != MmsysErrBadDeviceId && result != JoyErrUnplugged) break;
            }
            return snapshot;
        }

        private void MergeJoystick(JoySnapshot snapshot, JOYINFOEX info)
        {
            var dead = (uint)(AxisCenter * Deadzone);

            // POV 帽开关（八方向）
            if (info.dwPOV != JoyUndefinedPov)
            {
                var deg = info.dwPOV / 100.0;
                if (deg >= 315 || deg <= 45) snapshot.Up = true;
                if (deg >= 45 && deg <= 135) snapshot.Right = true;
                if (deg >= 135 && deg <= 225) snapshot.Down = true;
                if (deg >= 225 && deg <= 315) snapshot.Left = true;
            }

            // 模拟轴（X 左右，Y 上下；Y：0=上）
            if (info.dwXpos < AxisCenter - dead) snapshot.Left = true;
            if (info.dwXpos > AxisCenter + dead) snapshot.Right = true;
            if (info.dwYpos < AxisCenter - dead) snapshot.Up = true;
            if (info.dwYpos > AxisCenter + dead) snapshot.Down = true;

            // 前 4 个按键：bit0=A(轻拳) bit1=B(轻脚) bit2=C(重拳) bit3=D(重脚)
            snapshot.LightPunch = (info.dwButtons & 0x01) != 0;
            snapshot.LightKick = (info.dwButtons & 0x02) != 0;
            snapshot.HeavyPunch = (info.dwButtons & 0x04) != 0;
            snapshot.HeavyKick = (info.dwButtons & 0x08) != 0;
        }

        private static uint GetState(uint index, out XInputState state)
        {
            if (_use14)
            {
                try
                {
                    var result = XInputGetState14(index, out state);
                    return result;
                }
                catch (DllNotFoundException)
                {
                    _use14 = false;
                }
            }
            return XInputGetState13(index, out state);
        }

        private static bool Same(GamepadSnapshot a, GamepadSnapshot b) =>
            a.Up == b.Up && a.Down == b.Down && a.Left == b.Left && a.Right == b.Right &&
            a.LightPunch == b.LightPunch && a.HeavyPunch == b.HeavyPunch &&
            a.LightKick == b.LightKick && a.HeavyKick == b.HeavyKick;

        public void Dispose() => _timer?.Dispose();

        private sealed class XInputSnapshot
        {
            public bool Connected;
            public int Slot = -1;
            public bool Up, Down, Left, Right, LightPunch, HeavyPunch, LightKick, HeavyKick;
        }

        private sealed class JoySnapshot
        {
            public bool Connected;
            public bool Up, Down, Left, Right, LightPunch, HeavyPunch, LightKick, HeavyKick;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputState { public uint PacketNumber; public XInputGamepad Gamepad; }

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputGamepad
        {
            public ushort Buttons;
            public byte LeftTrigger;
            public byte RightTrigger;
            public short ThumbLX;
            public short ThumbLY;
            public short ThumbRX;
            public short ThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOYINFOEX
        {
            public uint dwSize;
            public uint dwFlags;
            public uint dwXpos;
            public uint dwYpos;
            public uint dwZpos;
            public uint dwRpos;
            public uint dwUpos;
            public uint dwVpos;
            public uint dwButtons;
            public uint dwButtonNumber;
            public uint dwPOV;
            public uint dwReserved1;
            public uint dwReserved2;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState14(uint userIndex, out XInputState state);

        [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState13(uint userIndex, out XInputState state);

        [DllImport("winmm.dll")]
        private static extern uint joyGetPosEx(uint uJoyID, ref JOYINFOEX pji);
    }
}
