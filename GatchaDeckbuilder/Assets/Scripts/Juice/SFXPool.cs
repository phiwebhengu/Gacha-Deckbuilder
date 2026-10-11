using UnityEngine;

public static class SFXPool
{
    private const int PoolSize = 12;

    private static AudioSource[] sources;
    private static int next;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        sources = null;
        next = 0;
    }

    private static void EnsureCreated()
    {
        if (sources != null && sources[0] != null) return;

        var go = new GameObject("[SfxPool]");
        Object.DontDestroyOnLoad(go);

        sources = new AudioSource[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f; // 2D sound
            sources[i] = s;
        }
        next = 0;
    }

    public static void Play(AudioClip clip, float pitch = 1f, float volume = 1f)
    {
        if (clip == null) return;
        EnsureCreated();

        // Prefer a source that is idle so we never re-pitch a sound that's mid-playback.
        AudioSource chosen = null;
        for (int i = 0; i < PoolSize; i++)
        {
            int idx = (next + i) % PoolSize;
            if (!sources[idx].isPlaying)
            {
                chosen = sources[idx];
                next = (idx + 1) % PoolSize;
                break;
            }
        }

        // Everything busy (very rare): reuse the next one round-robin.
        if (chosen == null)
        {
            chosen = sources[next];
            next = (next + 1) % PoolSize;
        }

        chosen.pitch = pitch;
        chosen.PlayOneShot(clip, volume);
    }
}