using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using RipNRage.Engine.Platform;

namespace RipNRage.Engine.Audio
{
    /// <summary>Minimal OpenAL 1.1 binding.</summary>
    internal static unsafe class AL
    {
        private const string Lib = NativeLoader.OpenAlLib;

        public const int FORMAT_MONO8 = 0x1100, FORMAT_MONO16 = 0x1101, FORMAT_STEREO8 = 0x1102, FORMAT_STEREO16 = 0x1103;
        public const int BUFFER = 0x1009, GAIN = 0x100A, PITCH = 0x1003, LOOPING = 0x1007, SOURCE_STATE = 0x1010, PLAYING = 0x1012;
        public const int POSITION = 0x1004, SOURCE_RELATIVE = 0x202;

        [DllImport(Lib, EntryPoint = "alcOpenDevice", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr alcOpenDevice(IntPtr name);
        [DllImport(Lib, EntryPoint = "alcCloseDevice", CallingConvention = CallingConvention.Cdecl)] public static extern byte alcCloseDevice(IntPtr device);
        [DllImport(Lib, EntryPoint = "alcCreateContext", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr alcCreateContext(IntPtr device, int* attrs);
        [DllImport(Lib, EntryPoint = "alcMakeContextCurrent", CallingConvention = CallingConvention.Cdecl)] public static extern byte alcMakeContextCurrent(IntPtr ctx);
        [DllImport(Lib, EntryPoint = "alcDestroyContext", CallingConvention = CallingConvention.Cdecl)] public static extern void alcDestroyContext(IntPtr ctx);
        [DllImport(Lib, EntryPoint = "alGenBuffers", CallingConvention = CallingConvention.Cdecl)] public static extern void alGenBuffers(int n, uint* buffers);
        [DllImport(Lib, EntryPoint = "alDeleteBuffers", CallingConvention = CallingConvention.Cdecl)] public static extern void alDeleteBuffers(int n, uint* buffers);
        [DllImport(Lib, EntryPoint = "alBufferData", CallingConvention = CallingConvention.Cdecl)] public static extern void alBufferData(uint buffer, int format, void* data, int size, int freq);
        [DllImport(Lib, EntryPoint = "alGenSources", CallingConvention = CallingConvention.Cdecl)] public static extern void alGenSources(int n, uint* sources);
        [DllImport(Lib, EntryPoint = "alDeleteSources", CallingConvention = CallingConvention.Cdecl)] public static extern void alDeleteSources(int n, uint* sources);
        [DllImport(Lib, EntryPoint = "alSourcei", CallingConvention = CallingConvention.Cdecl)] public static extern void alSourcei(uint source, int param, int value);
        [DllImport(Lib, EntryPoint = "alSourcef", CallingConvention = CallingConvention.Cdecl)] public static extern void alSourcef(uint source, int param, float value);
        [DllImport(Lib, EntryPoint = "alSource3f", CallingConvention = CallingConvention.Cdecl)] public static extern void alSource3f(uint source, int param, float x, float y, float z);
        [DllImport(Lib, EntryPoint = "alSourcePlay", CallingConvention = CallingConvention.Cdecl)] public static extern void alSourcePlay(uint source);
        [DllImport(Lib, EntryPoint = "alSourceStop", CallingConvention = CallingConvention.Cdecl)] public static extern void alSourceStop(uint source);
        [DllImport(Lib, EntryPoint = "alGetSourcei", CallingConvention = CallingConvention.Cdecl)] public static extern void alGetSourcei(uint source, int param, int* value);
        [DllImport(Lib, EntryPoint = "alListenerf", CallingConvention = CallingConvention.Cdecl)] public static extern void alListenerf(int param, float value);
        [DllImport(Lib, EntryPoint = "alGetError", CallingConvention = CallingConvention.Cdecl)] public static extern int alGetError();
    }

    /// <summary>PCM audio data (UnityEngine.AudioClip stand-in). Loads 8/16-bit PCM WAV files.</summary>
    public sealed class AudioClip : EngineObject
    {
        private static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);

        internal uint Buffer;
        public int channels { get; private set; }
        public int frequency { get; private set; }
        public float length { get; private set; }
        public bool IsValid => Buffer != 0;

        private AudioClip() { }

        /// <summary>Loads a WAV (returns a silent placeholder and logs a warning for unsupported formats).</summary>
        public static AudioClip Load(string path)
        {
            string full = Path.GetFullPath(path);
            if (Cache.TryGetValue(full, out var c)) return c;
            var clip = new AudioClip { name = Path.GetFileNameWithoutExtension(full) };
            Cache[full] = clip;
            try
            {
                if (!File.Exists(full)) { Debug.LogWarning("Audio file not found: " + full); return clip; }
                if (!full.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogWarning($"Audio format not supported yet ({Path.GetExtension(full)}): {Path.GetFileName(full)} — only PCM WAV is decoded.");
                    return clip;
                }
                clip.LoadWav(full);
            }
            catch (Exception e) { Debug.LogWarning($"Failed to load audio {full}: {e.Message}"); }
            return clip;
        }

        private unsafe void LoadWav(string path)
        {
            using var br = new BinaryReader(File.OpenRead(path));
            if (new string(br.ReadChars(4)) != "RIFF") throw new InvalidDataException("Not a RIFF file");
            br.ReadUInt32();
            if (new string(br.ReadChars(4)) != "WAVE") throw new InvalidDataException("Not a WAVE file");
            int fmtChannels = 0, sampleRate = 0, bits = 0, audioFormat = 0;
            byte[] data = null;
            while (br.BaseStream.Position + 8 <= br.BaseStream.Length)
            {
                string id = new string(br.ReadChars(4));
                int size = br.ReadInt32();
                long next = br.BaseStream.Position + size + (size & 1);
                if (id == "fmt ")
                {
                    audioFormat = br.ReadInt16();
                    fmtChannels = br.ReadInt16();
                    sampleRate = br.ReadInt32();
                    br.ReadInt32(); br.ReadInt16();
                    bits = br.ReadInt16();
                    if (audioFormat == 0xFFFE && size >= 26) { br.ReadInt16(); br.ReadInt16(); br.ReadInt32(); audioFormat = br.ReadInt16(); }
                }
                else if (id == "data") data = br.ReadBytes(size);
                br.BaseStream.Position = next;
                if (data != null && fmtChannels != 0) break;
            }
            if (data == null || fmtChannels == 0) throw new InvalidDataException("WAV has no data/fmt chunk");

            // Convert float / 24 / 32-bit PCM to 16-bit so OpenAL can play it.
            if (audioFormat == 3 && bits == 32) data = FloatTo16(data);
            else if (audioFormat == 1 && bits == 24) data = Int24To16(data);
            else if (audioFormat == 1 && bits == 32) data = Int32To16(data);
            else if (audioFormat != 1 || (bits != 8 && bits != 16)) throw new NotSupportedException($"WAV format {audioFormat}/{bits}-bit");
            if (bits != 8) bits = 16;

            channels = fmtChannels; frequency = sampleRate;
            length = data.Length / (float)(sampleRate * fmtChannels * (bits / 8));
            if (!AudioEngine.IsInitialized) return;
            int format = fmtChannels == 1 ? (bits == 8 ? AL.FORMAT_MONO8 : AL.FORMAT_MONO16) : (bits == 8 ? AL.FORMAT_STEREO8 : AL.FORMAT_STEREO16);
            uint buf; AL.alGenBuffers(1, &buf);
            fixed (byte* p = data) AL.alBufferData(buf, format, p, data.Length, sampleRate);
            Buffer = buf;
        }

        private static byte[] FloatTo16(byte[] src)
        {
            int n = src.Length / 4; var dst = new byte[n * 2];
            for (int i = 0; i < n; i++)
            {
                float f = BitConverter.ToSingle(src, i * 4);
                short s = (short)Mathf.Clamp(f * 32767f, -32768f, 32767f);
                dst[i * 2] = (byte)s; dst[i * 2 + 1] = (byte)(s >> 8);
            }
            return dst;
        }

        private static byte[] Int24To16(byte[] src)
        {
            int n = src.Length / 3; var dst = new byte[n * 2];
            for (int i = 0; i < n; i++) { dst[i * 2] = src[i * 3 + 1]; dst[i * 2 + 1] = src[i * 3 + 2]; }
            return dst;
        }

        private static byte[] Int32To16(byte[] src)
        {
            int n = src.Length / 4; var dst = new byte[n * 2];
            for (int i = 0; i < n; i++) { dst[i * 2] = src[i * 4 + 2]; dst[i * 2 + 1] = src[i * 4 + 3]; }
            return dst;
        }
    }

    /// <summary>Global OpenAL device/context.</summary>
    public static unsafe class AudioEngine
    {
        private static IntPtr _device, _context;
        public static bool IsInitialized { get; private set; }
        public static float masterVolume = 1f;

        public static void Initialize()
        {
            if (IsInitialized) return;
            try
            {
                NativeLoader.Register();
                _device = AL.alcOpenDevice(IntPtr.Zero);
                if (_device == IntPtr.Zero) { Debug.LogWarning("OpenAL: no audio device; sound disabled."); return; }
                _context = AL.alcCreateContext(_device, null);
                if (_context == IntPtr.Zero || AL.alcMakeContextCurrent(_context) == 0) { Debug.LogWarning("OpenAL: context creation failed; sound disabled."); return; }
                IsInitialized = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("OpenAL unavailable (" + e.GetType().Name + "); sound disabled.");
            }
        }

        public static void Shutdown()
        {
            if (!IsInitialized) return;
            AL.alcMakeContextCurrent(IntPtr.Zero);
            AL.alcDestroyContext(_context);
            AL.alcCloseDevice(_device);
            IsInitialized = false;
        }
    }

    /// <summary>UnityEngine.AudioSource subset: Play / PlayOneShot / Stop / clip / volume / pitch / loop.</summary>
    public sealed unsafe class AudioSource : Behaviour
    {
        private uint _source;
        private readonly List<uint> _oneShots = new List<uint>();

        public AudioClip clip;
        public float volume = 1f;
        public float pitch = 1f;
        public bool loop;
        public bool playOnAwake;
        public bool spatial;

        protected override void Awake()
        {
            if (!AudioEngine.IsInitialized) return;
            uint s; AL.alGenSources(1, &s); _source = s;
            AL.alSourcei(_source, AL.SOURCE_RELATIVE, 1);
            if (playOnAwake && clip != null) Play();
        }

        public bool isPlaying
        {
            get
            {
                if (_source == 0) return false;
                int st; AL.alGetSourcei(_source, AL.SOURCE_STATE, &st);
                return st == AL.PLAYING;
            }
        }

        public void Play()
        {
            if (_source == 0 || clip == null || !clip.IsValid) return;
            AL.alSourceStop(_source);
            AL.alSourcei(_source, AL.BUFFER, (int)clip.Buffer);
            AL.alSourcef(_source, AL.GAIN, volume * AudioEngine.masterVolume);
            AL.alSourcef(_source, AL.PITCH, pitch);
            AL.alSourcei(_source, AL.LOOPING, loop ? 1 : 0);
            AL.alSourcePlay(_source);
        }

        public void Stop()
        {
            if (_source != 0) AL.alSourceStop(_source);
        }

        /// <summary>Plays a clip without interrupting the main clip (Unity semantics).</summary>
        public void PlayOneShot(AudioClip oneShot, float volumeScale = 1f)
        {
            if (!AudioEngine.IsInitialized || oneShot == null || !oneShot.IsValid) return;
            // recycle finished one-shot sources
            uint src = 0;
            for (int i = _oneShots.Count - 1; i >= 0; i--)
            {
                int st; AL.alGetSourcei(_oneShots[i], AL.SOURCE_STATE, &st);
                if (st != AL.PLAYING) { src = _oneShots[i]; break; }
            }
            if (src == 0)
            {
                if (_oneShots.Count >= 8) return;
                uint s; AL.alGenSources(1, &s); src = s; _oneShots.Add(src);
                AL.alSourcei(src, AL.SOURCE_RELATIVE, 1);
            }
            AL.alSourceStop(src);
            AL.alSourcei(src, AL.BUFFER, (int)oneShot.Buffer);
            AL.alSourcef(src, AL.GAIN, volume * volumeScale * AudioEngine.masterVolume);
            AL.alSourcef(src, AL.PITCH, pitch);
            AL.alSourcei(src, AL.LOOPING, 0);
            AL.alSourcePlay(src);
        }

        protected override void OnDestroy()
        {
            if (!AudioEngine.IsInitialized) return;
            if (_source != 0) { uint s = _source; AL.alSourceStop(s); AL.alDeleteSources(1, &s); _source = 0; }
            foreach (var o in _oneShots) { uint s = o; AL.alSourceStop(s); AL.alDeleteSources(1, &s); }
            _oneShots.Clear();
        }
    }
}
