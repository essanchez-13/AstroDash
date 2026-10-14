using UnityEngine;

/// <summary>
/// Meteorito con SPRITES REALES (hoja asteroides.png). Tres clases:
///  - Pequeño (9 sprites): 1 impacto, tamaño 0.5–0.75 unidades.
///  - Grande gris (4 sprites): 1 impacto, tamaño 0.9–1.25.
///  - Grande ROJO (4 sprites): 2 impactos (se oscurece al primer golpe).
/// El Spawner llama a Setup() y aquí se elige clase, sprite aleatorio
/// dentro de la clase, tamaño y ajuste automático del collider a la
/// imagen elegida.
///
/// Variante PORTADOR (SetupCarrier): usa un sprite rojo más grande,
/// aguanta 3 impactos y PULSA con el color del power-up que contiene.
/// Al romperse avisa por OnCarrierDestroyed y el Spawner suelta la
/// cápsula en ese punto.
/// </summary>
public class Meteor : MonoBehaviour
{
    [Header("Sprites (hoja asteroides.png — los asigna la Fase 5)")]
    [SerializeField] private Sprite[] bigRedSprites;   // 2 impactos
    [SerializeField] private Sprite[] bigGreySprites;  // 1 impacto, grande
    [SerializeField] private Sprite[] smallSprites;    // 1 impacto, pequeño

    [Header("Versión dañada de cada rojo (hoja rotos.png — Fase 8)")]
    [Tooltip("Mismo orden que bigRedSprites: rotos[i] es la versión rota de rojos[i]")]
    [SerializeField] private Sprite[] bigRedBrokenSprites;

    /// El Spawner escucha esto para soltar la cápsula donde murió el portador
    public static event System.Action<Vector3, PowerUpType> OnCarrierDestroyed;

    // Colores de los fragmentos al destruirse (a juego con el arte)
    private static readonly Color RedFx = new Color(0.85f, 0.42f, 0.20f);
    private static readonly Color GreyFx = new Color(0.62f, 0.63f, 0.66f);
    private Color fxColor;

    private int redIndex = -1;   // qué sprite rojo se eligió (para su versión rota)
    private float worldSize;     // tamaño de mundo aplicado (para re-escalar al romperse)

    private float fallSpeed;
    private float drift;      // deriva lateral
    private float spin;       // grados/seg de rotación
    private int hp;
    private bool isCarrier;
    private PowerUpType contained;   // power-up que lleva el portador
    private SpriteRenderer sr;
    private CircleCollider2D circle;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        circle = GetComponent<CircleCollider2D>();
    }

    /// GENERACIÓN ALEATORIA — la llama el Spawner en cada aparición:
    /// clase (pequeño/gris/rojo), sprite concreto y tamaño varían.
    public void Setup(GameConfig cfg, float difficulty)
    {
        CommonSetup(cfg, difficulty);
        isCarrier = false;
        sr.color = Color.white;

        if (Random.value < cfg.smallChance)
        {
            hp = 1;
            fxColor = GreyFx;
            redIndex = -1;
            ApplySprite(Pick(smallSprites), Random.Range(cfg.smallSizeMin, cfg.smallSizeMax));
        }
        else
        {
            bool red = Random.value < cfg.redChance;   // rojo = aguanta 2
            hp = red ? 2 : 1;
            fxColor = red ? RedFx : GreyFx;
            float size = Random.Range(cfg.bigSizeMin, cfg.bigSizeMax);
            if (red)
            {
                // Se recuerda CUÁL rojo salió para poder cambiarlo por su
                // versión rota exacta al recibir el primer impacto
                redIndex = Random.Range(0, bigRedSprites.Length);
                ApplySprite(bigRedSprites[redIndex], size);
            }
            else
            {
                redIndex = -1;
                ApplySprite(Pick(bigGreySprites), size);
            }
        }
    }

    /// Variante PORTADOR: sprite rojo, más grande, 3 de vida y un
    /// power-up aleatorio dentro
    public void SetupCarrier(GameConfig cfg)
    {
        CommonSetup(cfg, 0f);
        fallSpeed *= 0.9f;   // algo más lento: es un objetivo, no una amenaza
        drift *= 0.7f;
        spin = Random.Range(-60f, 60f);

        isCarrier = true;
        contained = (PowerUpType)Random.Range(0, 5);
        hp = cfg.carrierHp;
        fxColor = PowerUpItem.GetColor(contained);   // estalla en "su" color
        redIndex = Random.Range(0, bigRedSprites.Length);
        ApplySprite(bigRedSprites[redIndex], cfg.carrierScale);
    }

    // Parámetros comunes de movimiento
    private void CommonSetup(GameConfig cfg, float difficulty)
    {
        transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        // La dificultad (0→1) acelera la caída, igual que en el prototipo
        fallSpeed = Random.Range(cfg.meteorSpeedMin, cfg.meteorSpeedMax)
                    * (1f + difficulty * cfg.meteorSpeedRamp);
        drift = Random.Range(-cfg.meteorDriftMax, cfg.meteorDriftMax);
        spin = Random.Range(-90f, 90f);
    }

    private static Sprite Pick(Sprite[] sprites) => sprites[Random.Range(0, sprites.Length)];

    /// Asigna el sprite, escala el transform para que la imagen mida
    /// `size` unidades de mundo (los sprites de la hoja tienen
    /// tamaños en píxeles distintos) y AJUSTA EL COLLIDER a la imagen:
    /// radio = mitad del lado menor × 0.85 (hitbox "justa").
    private void ApplySprite(Sprite s, float size)
    {
        worldSize = size;   // recordado para re-aplicar al cambiar a la versión rota
        sr.sprite = s;
        Vector2 b = s.bounds.size;      // tamaño del sprite en unidades locales
        transform.localScale = Vector3.one * (size / Mathf.Max(b.x, b.y));
        circle.radius = Mathf.Min(b.x, b.y) * 0.5f * 0.85f;
        circle.offset = s.bounds.center;
    }

    private void Update()
    {
        // Con el escudo del jugador activo, el mundo "avanza" más rápido
        float worldMult = PlayerPowerUps.WorldSpeedMult;
        transform.position += new Vector3(drift, -fallSpeed * worldMult, 0f) * Time.deltaTime;
        transform.Rotate(0f, 0f, spin * Time.deltaTime);

        // El portador pulsa hacia el color de su power-up (tinte sobre
        // el sprite rojo → se distingue de un rojo normal de lejos)
        if (isCarrier)
        {
            float t = (Mathf.Sin(Time.time * 6f) + 1f) * 0.5f;
            sr.color = Color.Lerp(Color.white, PowerUpItem.GetColor(contained), t * 0.65f);
        }

        // Rebote suave en los bordes laterales
        if (Mathf.Abs(transform.position.x) > ScreenBounds.HalfWidth - 0.3f)
            drift = -drift;

        // Salió por abajo sin ser destruido → reciclar sin puntos
        if (transform.position.y < -ScreenBounds.HalfHeight - 2f)
            GetComponent<PooledObject>().Release();
    }

    /// Destrucción por EMBESTIDA con el escudo activo: mismo premio que
    /// destruirlo a disparos. La llama PlayerController al chocar.
    public void ShieldRam() => Die();

    // Muerte común: puntos, fragmentos, aviso del portador y reciclaje
    private void Die()
    {
        GameManager.Instance.AddScore(GameManager.Instance.Config.scoreMeteor);
        if (isCarrier)
            OnCarrierDestroyed?.Invoke(transform.position, contained);
        if (FxManager.Instance != null)
            FxManager.Instance.Explode(transform.position, fxColor);
        GetComponent<PooledObject>().Release();
    }

    /// COLISIÓN PROYECTIL vs METEORITO — el motor la detecta por nosotros.
    /// (La colisión con el jugador la gestiona PlayerController.)
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Bullet")) return;

        // La bala de Potencia hace más daño que la normal
        hp -= other.GetComponent<Bullet>().Damage;

        // El proyectil siempre se consume
        other.GetComponent<PooledObject>().Release();

        if (hp <= 0)
        {
            Die();
        }
        else if (hp == 1 && redIndex >= 0 &&
                 bigRedBrokenSprites != null && redIndex < bigRedBrokenSprites.Length)
        {
            // FEEDBACK DE DAÑO: al quedarle un impacto, la roca roja se
            // cambia por su versión ROTA (mismo índice, con escombros).
            // También aplica al portador en su último punto de vida.
            ApplySprite(bigRedBrokenSprites[redIndex], worldSize);
        }
    }
}
