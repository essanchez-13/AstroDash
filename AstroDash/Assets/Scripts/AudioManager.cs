using UnityEngine;

/// <summary>
/// Audio del juego (singleton en la escena).
///
/// MÚSICA: arrastra tu pista al campo "Music Clip" del Inspector del
/// objeto AudioManager y sonará en bucle automáticamente al iniciar.
/// Sin pista asignada no hace nada (no rompe).
///
/// SFX: el segundo AudioSource ya queda preparado — cuando toquen los
/// efectos (disparo, explosión, recogida) solo hay que llamar a
/// PlaySfx(clip) desde el punto correspondiente, sin montar nada más.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Música de fondo")]
    [Tooltip("Pista del MENÚ (también tienda y ajustes); suena en bucle al iniciar")]
    [SerializeField] private AudioClip musicClip;
    [Tooltip("Pista de la PARTIDA; sin asignar se usa la del menú")]
    [SerializeField] private AudioClip gameplayMusicClip;
    [Range(0f, 1f)]
    [SerializeField] private float musicVolume = 0.6f;
    [SerializeField] private AudioSource musicSource;

    [Header("Efectos de sonido (para la siguiente fase)")]
    [Range(0f, 1f)]
    [SerializeField] private float sfxVolume = 1f;
    [SerializeField] private AudioSource sfxSource;

    private const string MasterKey = "astrodash_master_volume";
    private const string MusicKey = "astrodash_music_volume";
    private const string SfxKey = "astrodash_sfx_volume";

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Tres volúmenes guardados: MAESTRO (AudioListener, afecta a todo),
        // MÚSICA y EFECTOS. Se restauran aquí (Awake) para que los sliders
        // del menú los lean ya aplicados.
        AudioListener.volume = PlayerPrefs.GetFloat(MasterKey, 1f);
        musicVolume = PlayerPrefs.GetFloat(MusicKey, musicVolume);
        sfxVolume = PlayerPrefs.GetFloat(SfxKey, sfxVolume);
    }

    public float MasterVolume => AudioListener.volume;

    /// Slider de VOLUMEN GENERAL: afecta a todo el audio a la vez
    public void SetMasterVolume(float volume)
    {
        AudioListener.volume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(MasterKey, AudioListener.volume);
    }

    public float MusicVolume => musicVolume;
    public float SfxVolume => sfxVolume;

    /// Slider de MÚSICA: se aplica al AudioSource en vivo
    public void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
        musicSource.volume = musicVolume;
        PlayerPrefs.SetFloat(MusicKey, musicVolume);
    }

    /// Slider de EFECTOS: los próximos PlaySfx salen con este nivel
    public void SetSfxVolume(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(SfxKey, sfxVolume);
    }

    private void Start()
    {
        musicSource.loop = true;
        musicSource.volume = musicVolume;
        if (musicClip != null)
            PlayMusic(musicClip);
    }

    /// Cambia la pista en caliente (menú vs partida) con FADE-IN: entra
    /// de menos a más para que el cambio no golpee (p. ej. al salir de
    /// la partida al menú).
    public void PlayMusic(AudioClip clip)
    {
        if (clip == null) return;
        musicSource.clip = clip;
        musicSource.Play();
        StartFadeIn();
    }

    /// MENÚ (y tienda/ajustes, que son paneles sobre el menú)
    public void PlayMenuMusic() => PlayMusic(musicClip);

    /// PARTIDA: su propia pista, desde el inicio; si no hay, la del menú
    public void PlayGameplayMusic()
        => PlayMusic(gameplayMusicClip != null ? gameplayMusicClip : musicClip);

    /// GAME OVER y PAUSA: la música calla (el game over queda en
    /// silencio hasta salir de él; la pausa reanuda con ResumeMusic).
    public void PauseMusic() => musicSource.Pause();

    /// Vuelta de la PAUSA: sigue donde iba, también con fade-in
    public void ResumeMusic()
    {
        if (musicSource.clip == null || musicSource.isPlaying) return;
        musicSource.UnPause();
        StartFadeIn();
    }

    private Coroutine fadeRoutine;
    private const float FadeInTime = 1.4f;

    private void StartFadeIn()
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeInRoutine());
    }

    // Tiempo SIN escalar: el fade corre igual aunque la pausa congele
    // el juego con timeScale = 0
    private System.Collections.IEnumerator FadeInRoutine()
    {
        float t = 0f;
        while (t < FadeInTime)
        {
            t += Time.unscaledDeltaTime;
            musicSource.volume = musicVolume * Mathf.Clamp01(t / FadeInTime);
            yield return null;
        }
        musicSource.volume = musicVolume;
        fadeRoutine = null;
    }

    /// Efecto one-shot: varios pueden sonar superpuestos sin cortarse
    public void PlaySfx(AudioClip clip, float volumeScale = 1f)
    {
        if (clip == null) return;
        sfxSource.PlayOneShot(clip, sfxVolume * volumeScale);
    }

}
