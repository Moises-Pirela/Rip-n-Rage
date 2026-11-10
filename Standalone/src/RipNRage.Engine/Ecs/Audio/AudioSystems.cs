using System;
using System.Collections.Generic;
using RipNRage.Engine.Platform;
using AudioClip = RipNRage.Engine.Audio.AudioClip;
using AudioEngine = RipNRage.Engine.Audio.AudioEngine;
using AL = RipNRage.Engine.Audio.AL;

namespace RipNRage.Engine.Ecs.Audio
{
    /// <summary>The clips audio components and events reference by handle (configs intern by asset path).</summary>
    public static class AudioAssets
    {
        public static readonly AssetRegistry<AudioClip> Clips = new AssetRegistry<AudioClip>();
        public static AssetHandle<AudioClip> Load(string path) => Clips.GetOrLoad(path, p => AudioClip.Load(Application.AssetPath(p)));
    }

    /// <summary>A fire-and-forget sound (listener-relative). Post it from any system; <see cref="Sfx"/> posts it from scripts.</summary>
    public struct PlaySoundEvent : IPostProcessEvent
    {
        public AssetHandle<AudioClip> Clip;
        public float Volume, Pitch;
    }

    /// <summary>
    /// A persistent voice owned by an entity (engine hums, loops, a sound that must stop with its owner). Set
    /// <see cref="Playing"/> to start / stop; the voice is released when the component or the entity goes.
    /// </summary>
    public struct AudioEmitter : IComponent
    {
        public AssetHandle<AudioClip> Clip;
        public float Volume, Pitch;
        public bool Loop, Playing;
    }

    public sealed class AudioEmitterConfig : ComponentConfig<AudioEmitter>
    {
        /// <summary>Asset path of the clip (wav / mp3 under assets).</summary>
        public string clip;
        public float volume = 1f, pitch = 1f;
        public bool loop, playOnStart;
        public override AudioEmitter Bake() => new AudioEmitter
        {
            Clip = string.IsNullOrEmpty(clip) ? default : AudioAssets.Load(clip),
            Volume = volume, Pitch = pitch, Loop = loop, Playing = playOnStart,
        };
    }

    /// <summary>Script-side entry point for one-shots: posts a <see cref="PlaySoundEvent"/> to the application's world.</summary>
    public static class Sfx
    {
        public static void Play(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (clip == null || Application.Current?.World == null) return;
            Application.Current.World.Post(new PlaySoundEvent { Clip = AudioAssets.Clips.Intern(clip), Volume = volume, Pitch = pitch });
        }
    }

    /// <summary>
    /// Owns every OpenAL voice: a pool for one-shots (oldest finished voice reused; dropped when all
    /// <see cref="MaxOneShots"/> are busy) and one voice per playing <see cref="AudioEmitter"/>.
    /// With no audio device (headless, --mute) it consumes the events and does nothing.
    /// </summary>
    public sealed unsafe class AudioSystem : SystemBase, IConsumes<PlaySoundEvent>, ILateTick, IOnEntityDestroyed, IOnSimEnd
    {
        public const int MaxOneShots = 32;
        private readonly List<uint> _pool = new List<uint>();
        private int _next;
        private readonly Dictionary<Entity, (uint source, AssetHandle<AudioClip> clip)> _voices = new Dictionary<Entity, (uint, AssetHandle<AudioClip>)>();
        private readonly List<Entity> _gone = new List<Entity>();
        private Query _emitters;

        /// <summary>One-shots played / dropped (all voices busy) since start (diagnostics, tests).</summary>
        public int Played { get; private set; }
        public int Dropped { get; private set; }

        public override void OnCreate() => _emitters = World.Query().WithReadOnly<AudioEmitter>().Build();

        public void Consume(SystemContext ctx, ReadOnlySpan<PlaySoundEvent> events)
        {
            foreach (var e in events)
            {
                var clip = AudioAssets.Clips.Resolve(e.Clip);
                if (clip == null) continue;
                if (!AudioEngine.IsInitialized) { Played++; continue; }
                uint src = Voice();
                if (src == 0) { Dropped++; continue; }
                Start(src, clip, e.Volume, e.Pitch <= 0f ? 1f : e.Pitch, false);
                Played++;
            }
        }

        private uint Voice()
        {
            for (int k = 0; k < _pool.Count; k++)
            {
                int i = (_next + k) % _pool.Count;
                int st; AL.alGetSourcei(_pool[i], AL.SOURCE_STATE, &st);
                if (st != AL.PLAYING) { _next = (i + 1) % Math.Max(1, _pool.Count); return _pool[i]; }
            }
            if (_pool.Count >= MaxOneShots) return 0;
            uint s = NewSource(); _pool.Add(s);
            return s;
        }

        private static uint NewSource()
        {
            uint s; AL.alGenSources(1, &s);
            AL.alSourcei(s, AL.SOURCE_RELATIVE, 1);
            return s;
        }

        private static void Start(uint src, AudioClip clip, float volume, float pitch, bool loop)
        {
            if (!clip.IsValid) return;
            AL.alSourceStop(src);
            AL.alSourcei(src, AL.BUFFER, (int)clip.Buffer);
            AL.alSourcef(src, AL.GAIN, volume * AudioEngine.masterVolume);
            AL.alSourcef(src, AL.PITCH, pitch);
            AL.alSourcei(src, AL.LOOPING, loop ? 1 : 0);
            AL.alSourcePlay(src);
        }

        public void LateTick(SystemContext ctx)
        {
            if (!AudioEngine.IsInitialized) return;
            foreach (var chunk in _emitters)
            {
                var em = chunk.Get<AudioEmitter>(); var ents = chunk.Entities;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (!chunk.Includes(i)) continue;
                    ref readonly var a = ref em[i];
                    bool has = _voices.TryGetValue(ents[i], out var v);
                    if (a.Playing && (!has || !v.clip.Equals(a.Clip)))
                    {
                        var clip = AudioAssets.Clips.Resolve(a.Clip);
                        if (clip == null) continue;
                        uint src = has ? v.source : NewSource();
                        Start(src, clip, a.Volume, a.Pitch <= 0f ? 1f : a.Pitch, a.Loop);
                        _voices[ents[i]] = (src, a.Clip);
                    }
                    else if (!a.Playing && has) { Release(v.source); _voices.Remove(ents[i]); }
                    else if (has) { AL.alSourcef(v.source, AL.GAIN, a.Volume * AudioEngine.masterVolume); AL.alSourcef(v.source, AL.PITCH, a.Pitch <= 0f ? 1f : a.Pitch); }
                }
            }
            // emitters whose component was removed
            _gone.Clear();
            foreach (var kv in _voices) if (!World.IsAlive(kv.Key) || !World.Has<AudioEmitter>(kv.Key)) _gone.Add(kv.Key);
            foreach (var e in _gone) { Release(_voices[e].source); _voices.Remove(e); }
        }

        private static void Release(uint src) { AL.alSourceStop(src); AL.alDeleteSources(1, &src); }

        public void OnEntityDestroyed(SystemContext ctx, Entity e)
        {
            if (!_voices.TryGetValue(e, out var v)) return;
            if (AudioEngine.IsInitialized) Release(v.source);
            _voices.Remove(e);
        }

        public void OnSimEnd(SystemContext ctx)
        {
            if (AudioEngine.IsInitialized)
            {
                foreach (var v in _voices.Values) Release(v.source);
                foreach (var s in _pool) Release(s);
            }
            _voices.Clear(); _pool.Clear();
        }
    }
}
