using UnityEngine;

/// <summary>
/// Temporizadores de los power-ups activos, equivalente al objeto
/// `power` del prototipo. Vive en el Player.
///
/// ACUMULABLES: recoger un power-up del mismo tipo SUMA duración a su
/// contador, y tipos distintos corren en paralelo. Al empezar una
/// partida nueva todos se limpian.
/// </summary>
public class PlayerPowerUps : MonoBehaviour
{
    // Singleton ligero: lo consultan Shooter, CollectibleItem y el HUD
    public static PlayerPowerUps Instance { get; private set; }

    [Tooltip("Aura cian que se muestra mientras el escudo está activo")]
    [SerializeField] private GameObject shieldVisual;
    [Tooltip("Aura violeta del imán (si coincide con el escudo, se superponen)")]
    [SerializeField] private GameObject magnetVisual;

    [Header("Sonidos de recogida (los cablea la Fase 12)")]
    [Tooltip("El escudo tiene su propio sonido de 'velocidad'")]
    [SerializeField] private AudioClip shieldPickupClip;
    [Tooltip("Imán, Doble, Rápido, Potencia — en ese orden")]
    [SerializeField] private AudioClip[] pickupClips;
    [Range(0f, 1f)]
    [SerializeField] private float pickupVolume = 0.8f;

    // Un temporizador por tipo, indexado por el enum PowerUpType
    private readonly float[] timers = new float[5];

    private void Awake() => Instance = this;

    /// Multiplicador global de "avance" del mundo: con el escudo activo
    /// todo baja más rápido (turbo hacia adelante). Lo consultan
    /// Meteor, CollectibleItem, PowerUpItem y Spawner.
    public static float WorldSpeedMult
    {
        get
        {
            var i = Instance;
            if (i == null || GameManager.Instance.State != GameState.Playing) return 1f;
            return i.IsActive(PowerUpType.Shield)
                ? GameManager.Instance.Config.shieldWorldMult : 1f;
        }
    }

    private void Start()
    {
        GameManager.Instance.OnStateChanged += state =>
        {
            if (state == GameState.Playing)
                System.Array.Clear(timers, 0, timers.Length);
        };
    }

    public bool IsActive(PowerUpType type) => timers[(int)type] > 0f;

    public float Remaining(PowerUpType type) => timers[(int)type];

    /// Recogida: SUMA duración (aquí está la acumulación) y suena el
    /// clip del tipo: el escudo tiene el suyo propio; los demás usan
    /// los 3 sonidos separados de "Resto de Power Ups"
    public void Add(PowerUpType type)
    {
        timers[(int)type] += GameManager.Instance.Config.powerUpDuration;

        if (AudioManager.Instance != null)
        {
            var clip = type == PowerUpType.Shield
                ? shieldPickupClip
                : pickupClips[(int)type - 1];   // Magnet=0, Double=1, Rapid=2, Big=3
            AudioManager.Instance.PlaySfx(clip, pickupVolume);
        }
    }

    private void Update()
    {
        bool playing = GameManager.Instance.State == GameState.Playing;

        if (playing)
            for (int i = 0; i < timers.Length; i++)
                timers[i] = Mathf.Max(0f, timers[i] - Time.deltaTime);

        // Cada aura acompaña a su power-up; si ambos están activos las
        // dos capas se superponen (giran en sentidos opuestos, AuraSpin)
        UpdateAura(shieldVisual, PowerUpType.Shield, playing);
        UpdateAura(magnetVisual, PowerUpType.Magnet, playing);
    }

    // Muestra u oculta un aura; en los últimos segundos (shieldWarnTime)
    // PARPADEA para avisar de que ese power-up se acaba
    private void UpdateAura(GameObject visual, PowerUpType type, bool playing)
    {
        if (visual == null) return;
        bool show = playing && IsActive(type);
        if (show && Remaining(type) < GameManager.Instance.Config.shieldWarnTime)
            show = Mathf.FloorToInt(Time.time * 8f) % 2 == 0;
        visual.SetActive(show);
    }
}
