using UnityEngine;

/// <summary>
/// Cápsula de power-up que cae desde arriba. El Spawner le asigna un
/// tipo aleatorio con Setup(); cada tipo tiene su propio ICONO real
/// (hoja powerups.png): escudo, imán, disparo doble, disparo rápido
/// y potencia. El icono y el collider se ajustan en cada Setup().
///
/// Setup del prefab: SpriteRenderer (icono), tag "PowerUp",
/// CircleCollider2D (Is Trigger), Rigidbody2D Kinematic con Use Full
/// Kinematic Contacts.
/// </summary>
public class PowerUpItem : MonoBehaviour
{
    [Header("Iconos (hoja powerups.png — los asigna la Fase 6)")]
    [SerializeField] private Sprite[] typeSprites;   // indexados por PowerUpType

    [Tooltip("Halo suave tras el icono, teñido del color del tipo")]
    [SerializeField] private SpriteRenderer glow;

    // Indexados por PowerUpType (los usan el HUD y el meteorito portador)
    private static readonly Color[] TypeColors =
    {
        new Color(0.13f, 0.83f, 0.93f),   // Shield  #22D3EE (cian)
        new Color(0.65f, 0.55f, 0.98f),   // Magnet  #A78BFA (violeta)
        new Color(0.96f, 0.45f, 0.71f),   // Double  #F472B6 (rosa)
        new Color(0.98f, 0.57f, 0.24f),   // Rapid   #FB923C (naranja)
        new Color(0.29f, 0.87f, 0.50f),   // BigShot #4ADE80 (verde)
    };

    /// Color identificativo de un tipo (lo usa también el meteorito
    /// portador para pulsar con el color de lo que lleva dentro)
    public static Color GetColor(PowerUpType type) => TypeColors[(int)type];

    public PowerUpType Type { get; private set; }

    private SpriteRenderer sr;
    private CircleCollider2D circle;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        circle = GetComponent<CircleCollider2D>();
    }

    /// La llama el Spawner en cada aparición con un tipo aleatorio
    public void Setup(PowerUpType type)
    {
        Type = type;
        var s = typeSprites[(int)type];
        sr.sprite = s;
        sr.color = Color.white;   // el icono ya trae su color

        // Normalizar el tamaño (los recortes varían unos píxeles) y
        // ajustar el collider al icono circular. Radio 0.95x: la
        // recogida es un premio, mejor pecar de generosa.
        float worldSize = GameManager.Instance.Config.powerUpSize;
        Vector2 size = s.bounds.size;
        transform.localScale = Vector3.one * (worldSize / Mathf.Max(size.x, size.y));
        circle.radius = Mathf.Min(size.x, size.y) * 0.5f * 0.95f;
        circle.offset = s.bounds.center;

        // Aura sutil del color del tipo (el pulso lo anima Update)
        if (glow != null)
            glow.color = WithAlpha(GetColor(type), 0.14f);
    }

    private void Update_Glow()
    {
        // (separado para legibilidad; se llama desde Update)
        if (glow == null) return;
        // Pulso MUY sutil de alpha: presencia apenas perceptible
        float a = 0.10f + 0.08f * (Mathf.Sin(Time.time * 3f) + 1f) * 0.5f;
        glow.color = WithAlpha(GetColor(Type), a);
    }

    private static Color WithAlpha(Color c, float a)
    {
        c.a = a;
        return c;
    }

    private void Update()
    {
        Update_Glow();

        // Caída recta, más lenta que los meteoritos: el jugador decide
        // si le compensa desviarse para cogerlo (el turbo del escudo
        // también la acelera, como a todo el mundo)
        transform.position += Vector3.down *
            (GameManager.Instance.Config.powerUpFallSpeed
             * PlayerPowerUps.WorldSpeedMult * Time.deltaTime);

        if (transform.position.y < -ScreenBounds.HalfHeight - 1f)
            GetComponent<PooledObject>().Release();
    }

    /// La llama PlayerController al tocarlo
    public void Collect()
    {
        PlayerPowerUps.Instance.Add(Type);   // suma duración (acumulable)
        GetComponent<PooledObject>().Release();
    }
}
