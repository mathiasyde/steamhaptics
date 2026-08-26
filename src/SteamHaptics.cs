using System.Diagnostics;
using System.Text;
using HidApi;
using Microsoft.Extensions.Logging;

// Steam Haptics Streaming
// This only supports 8kHz unsigned 8-bit µ-law encoded audio to accomodate wireless mode using the puck
// The puck does not have enough bandwidth for full 16-bit audio

namespace SteamHaptics {
    using Sample = byte;

    using StereoSound = StereoFrame[];
    using MonoSound = MonoFrame[];

    public struct StereoFrame {
        public Sample[] Left = new Sample[31];
        public Sample[] Right = new Sample[31];

        public StereoFrame() {}
    }

    public struct MonoFrame {
        public Sample[] Stream = new Sample[31];

        public MonoFrame() {}

        internal StereoFrame AsStereo() {
            StereoFrame stereo = new StereoFrame();
            Array.Copy(Stream, stereo.Left, Stream.Length);
            Array.Copy(Stream, stereo.Right, Stream.Length);
            return stereo;
        }
    }

    public enum StreamMode : byte {
        Khz8_16Bit = 0,
        Khz4_16Bit = 1,
        Khz2_16Bit = 2,
        Khz1_16Bit = 3,
        Khz8_8Bit = 4,
        Khz4_8Bit = 5,
        Khz2_8Bit = 6,
        Khz1_8Bit = 7,
        Khz8_8BitULaw = 8,
        Khz4_8BitULaw = 9,
        Khz2_8BitULaw = 10,
        Khz1_8BitULaw = 11
    }

    public static class StreamModeExtensions {
        public static int SampleRate(this StreamMode mode) {
            return mode switch {
                StreamMode.Khz8_16Bit => 8000,
                StreamMode.Khz4_16Bit => 4000,
                StreamMode.Khz2_16Bit => 2000,
                StreamMode.Khz1_16Bit => 1000,
                StreamMode.Khz8_8Bit => 8000,
                StreamMode.Khz4_8Bit => 4000,
                StreamMode.Khz2_8Bit => 2000,
                StreamMode.Khz1_8Bit => 1000,
                StreamMode.Khz8_8BitULaw => 8000,
                StreamMode.Khz4_8BitULaw => 4000,
                StreamMode.Khz2_8BitULaw => 2000,
                StreamMode.Khz1_8BitULaw => 1000,
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
            };
        }

        public static int BitDepth(this StreamMode mode) {
            return mode switch {
                StreamMode.Khz8_16Bit => 16,
                StreamMode.Khz4_16Bit => 16,
                StreamMode.Khz2_16Bit => 16,
                StreamMode.Khz1_16Bit => 16,
                StreamMode.Khz8_8Bit => 8,
                StreamMode.Khz4_8Bit => 8,
                StreamMode.Khz2_8Bit => 8,
                StreamMode.Khz1_8Bit => 8,
                StreamMode.Khz8_8BitULaw => 8,
                StreamMode.Khz4_8BitULaw => 8,
                StreamMode.Khz2_8BitULaw => 8,
                StreamMode.Khz1_8BitULaw => 8,
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
            };
        }
    } 

    public class Utils {
        // load mono audio file and convert to SteamHaptics.Frame[] for streaming
        // this simply copies the bytes of the specified file into the SteamHaptics.Frame[] structure,
        // assuming the file is in the correct format (8kHz, 8-bit µ-law encoded)
        public static MonoSound Load(string path, int sampleRate = HapticStreamer.SampleRate) {
            var bytes = File.ReadAllBytes(path);
            var sampleCount = bytes.Length; // 1 byte per sample
            var frames = new MonoFrame[sampleCount / HapticStreamer.SamplesPerChannel];

            var table = CreateMuLawGainTable(2.0);

            for (int i = 0; i < frames.Length; i++) {
                var frame = new MonoFrame();

                for (int j = 0; j < HapticStreamer.SamplesPerChannel; j++) {
                    var byteValue = bytes[i * HapticStreamer.SamplesPerChannel + j];
                    byteValue = table[byteValue]; // apply gain table
                    frame.Stream[j] = byteValue;
                }

                frames[i] = frame;
            }

            return frames;
        }

        private const int MuLawBias = 0x84;
        private const int MuLawClip = 32635;

        public static byte LinearToMuLaw(short sample) {
            int pcm = sample;
            int sign = pcm < 0 ? 0x80 : 0;

            if (pcm < 0)
                pcm = -pcm;

            if (pcm > MuLawClip)
                pcm = MuLawClip;

            pcm += MuLawBias;
            int exponent = 7;

            for (int mask = 0x4000; (pcm & mask) == 0 && exponent > 0; mask >>= 1)
                exponent--;

            int mantissa = (pcm >> (exponent + 3)) & 0x0F;

            return (byte)~(sign | (exponent << 4) | mantissa);
        }

        public static short MuLawToLinear(byte value) {
            value = (byte)~value;

            int sign = value & 0x80;
            int exponent = (value >> 4) & 0x07;
            int mantissa = value & 0x0F;

            int sample = ((mantissa << 3) + MuLawBias) << exponent;
            sample -= MuLawBias;

            return (short)(sign != 0 ? -sample : sample);
        }

        static byte[] CreateMuLawGainTable(double gain) {
            var table = new byte[256];

            for (int i = 0; i < 256; i++) {
                short pcm = MuLawToLinear((byte)i);
                int amplified = (int)(pcm * gain);

                amplified = Math.Clamp(amplified, short.MinValue, short.MaxValue);
                table[i] = LinearToMuLaw((short)amplified);
            }

            return table;
        }
    }

    public static class HapticStreamer {
        public const int SampleRate = 8000; // always 8kHz for the Steam Controller
        public const int SamplesPerChannel = 31; // 31 samples per channel per report
        public const int ChannelCount = 2; // left and right channels

        public static CircularBuffer<StereoFrame> Buffer = new(512);

        private static Thread? StreamingThread;
        private static CancellationTokenSource Token = new CancellationTokenSource();

        public static void EnableStreaming(Device device, StreamMode mode) {
            device.Write(new byte[] { 0x86, 0x01, 0x02, 0x00 }); // STREAM_CONFIGURE STOP INT_BOTH
            device.Write(new byte[] { 0x86, 0x01, 0x05, 0x00 }); // STREAM_CONFIGURE STOP TP_BOTH

            Thread.Sleep(10);

            device.Write(new byte[] { 0x86, 0x02, 0x02, (byte)mode }); // STREAM_CONFIGURE START INT_BOTH <mode>
            device.Write(new byte[] { 0x86, 0x02, 0x05, (byte)mode }); // STREAM_CONFIGURE START TP_BOTH <mode>
        }

        public static void StopStreaming(Device device, bool wait = false) {
            while (wait && Buffer.Available > 0) {
                Thread.SpinWait(1);
            }

            Token.Cancel();

            device.Write(new byte[] { 0x86, 0x01, 0x02, 0x00 }); // STREAM_CONFIGURE STOP INT_BOTH
            device.Write(new byte[] { 0x86, 0x01, 0x05, 0x00 }); // STREAM_CONFIGURE STOP TP_BOTH
        }

        public static void StartStreaming(Device device, StreamMode mode = StreamMode.Khz8_8BitULaw) {
            EnableStreaming(device, mode);

            StreamingThread = new Thread(() => StreamingLoop(device, mode, Token.Token));
            StreamingThread.Start();
        }

        private static void StreamingLoop(Device device, StreamMode mode, CancellationToken token) {
            byte[] report = new byte[64];
            report[0] = 0x88;
            report[1] = SamplesPerChannel;

            long period = (long)((double)SamplesPerChannel / SampleRate * Stopwatch.Frequency);
            long deadline = Stopwatch.GetTimestamp();

            // byte[] input = new byte[64];

            while (token.IsCancellationRequested == false) {
                // TODO(mathias): device overflow/underflow acknowledge logic
                // if device reports overflow, continue the loop until device is ready to receive data again

                // int read = device.ReadTimeout(input, 1);
                // if (input[0] == 0x44) { // flow-control report
                //     byte actuator = input[1]; // 0 = internal left, 1 = internal right
                //     byte flags = input[2];

                //     bool bufferOverrun = (flags >> 0 & 0x01) == 1;
                //     bool bufferUnderrun = (flags >> 1 & 0x01) == 1;
                //     bool needsMoreData = (flags >> 2 & 0x01) == 1;
                //     bool hasEnoughData = (flags >> 3 & 0x01) == 1;
                //     bool configurationInvalid = (flags >> 4 & 0x01) == 1;
                //     bool configurationAccepted = (flags >> 5 & 0x01) == 1;
                //     bool configurationAlreadyRunning = (flags >> 6 & 0x01) == 1;
                // }


                if (Buffer.TryRead(out StereoFrame frame)) {
                    Array.Copy(frame.Left, 0, report, 2, SamplesPerChannel);
                    Array.Copy(frame.Right, 0, report, 33, SamplesPerChannel);

                    device.Write(report);
                }

                deadline += period;
                WaitUntil(deadline);
            }
        }

        private static void WaitUntil(long deadline) {
            while (true) {
                long remaining = deadline - Stopwatch.GetTimestamp();

                if (remaining <= 0)
                    return;

                Thread.SpinWait(1);
            }
        }

        private static void PrintReport(byte[] report) {
            StringBuilder sb = new();
            sb.Append($"{report[0]:X2}{report[1]:X2} | ");
            for (int i = 0; i < SamplesPerChannel; i++) {
                sb.Append($"{report[2 + i]:X2}");
            }
            sb.Append(" | ");
            for (int i = 0; i < SamplesPerChannel; i++) {
                sb.Append($"{report[33 + i]:X2}");
            }
            sb.AppendLine();
            Application.Logger.LogDebug("Sending report: {0}", sb.ToString());
        }
    }
}
