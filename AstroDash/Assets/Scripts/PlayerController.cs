using UnityEngine;

/// <summary>
/// La nave del jugador. Traducción directa de la lógica del prototipo:
/// - Persigue suavemente la X del dedo/ratón (interpolación exponencial).
/// - Flechas o A/D en escritorio para probar en el editor.
/// - Las colisiones ya NO se calculan a mano: el motor de físicas 2D
///   dispara OnTriggerEnter2D cuando dos colliders se superponen.
///
/// Setup del GameObject: SpriteRenderer (triángulo), CircleCollider2D
/// con "Is Trigger" activado y radio ~0.8x del visual (mismo truco de
/// justicia del prototipo), Rigidbody2D dinámico con Gravity Scale = 0.
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class PlayerController : MonoBehaviour
{
    private float targetX;
    private float startY;
    private Camera cam;
    private PlayerPowerUps powerUps;

    private Transform muzzle;
    private Transform engineFire;
    private CircleCollider2D circle;

    private void Awake()
    {
        cam = Camera.main;
        startY = transform.position.y;
        powerUps = GetComponent<PlayerPowerUps>();
        circle = GetComponent<CircleCollider2D>();
        muzzle = transform.Find("Muzzle");
        engineFire = transform.Find("EngineFire");
    }

    private void Start()
    {
        ApplyEquipment();

        // La nave solo se VE durante la partida: en el menú quedaba
        // detrás de los botones translúcidos (el diseño es una tarjeta
        // completa). El GameObject sigue activo; solo se apaga el sprite.
        var sr = GetComponent<SpriteRenderer>();
        sr.enabled = GameManager.Instance.State == GameState.Playing;

        GameManager.Instance.OnStateChanged += state =>
        {
            sr.enabled = state == GameState.Playing;
            if (state == GameState.Playing)
            {
                ApplyEquipment();
                // ENTRADA: la nave "baja" desde la zona del título hasta
                // su puesto (la animación corre en Update)
                entryProgress = 0f;
                transform.position = new Vector3(0f, startY + EntryDrop, 0f);
                targetX = 0f;
            }
        };
    }

    private const float EntryDrop = 8.6f;   // unidades por encima del puesto
    private float entryProgress = 1f;       // 1 = entrada terminada

    /// Aplica la NAVE EQUIPADA (comprada en la tienda): sprite, escala
    /// normalizada (todas miden lo mismo de ancho), hitbox constante y
    /// recolocación del cañón y del fuego del motor a su silueta.
    public void ApplyEquipment()
    {
        var cat = Catalog.Instance;
        if (cat == null) return;
        var ship = cat.GetShip(Progress.EquippedShip);
        GetComponent<SpriteRenderer>().sprite = ship.sprite;

        float scale = 0.62f / ship.sprite.bounds.size.x;
        transform.localScale = new Vector3(scale, scale, 1f);
        circle.radius = 0.22f / scale;   // 0.22 unidades de mundo, siempre

        float halfH = ship.sprite.bounds.extents.y;
        if (muzzle != null) muzzle.localPosition = new Vector3(0f, halfH + 0.15f, 0f);
        if (engineFire != null) engineFire.localPosition = new Vector3(0f, -halfH - 0.03f, 0f);
    }

    private void Update()
    {
        var gm = GameManager.Instance;
        if (gm.State != GameState.Playing) return;
        var cfg = gm.Config;

        // Animación de entrada: descenso con frenada suave (ease-out);
        // sin control del jugador hasta llegar al puesto (~0.7 s)
        if (entryProgress < 1f)
        {
            entryProgress = Mathf.Min(1f, entryProgress + Time.deltaTime / 0.7f);
            float ease = 1f - Mathf.Pow(1f - entryProgress, 3f);
            transform.position = new Vector3(0f, startY + EntryDrop * (1f - ease), 0f);
            targetX = 0f;
            return;
        }

        /* NOTA: el escudo ya no acelera el movimiento lateral — el
           "turbo" es hacia adelante (el mundo baja más rápido, ver
           PlayerPowerUps.WorldSpeedMult). El control se queda normal. */

        // --- Entrada táctil / ratón: apuntar a la X del puntero ---
        if (Input.GetMouseButton(0))
            targetX = cam.ScreenToWorldPoint(Input.mousePosition).x;

        // --- Entrada por teclado (editor y pruebas de escritorio) ---
        float keyboard = Input.GetAxisRaw("Horizontal"); // flechas o A/D
        if (keyboard != 0f)
            targetX += keyboard * cfg.playerSpeed * Time.deltaTime;

        // Mantener la nave dentro de la pantalla
        float halfW = ScreenBounds.HalfWidth - 0.4f;
        targetX = Mathf.Clamp(targetX, -halfW, halfW);

        // Interpolación suave hacia el objetivo (movimiento con "peso").
        // 1 - exp(-k*dt) es la versión independiente del framerate del
        // `(target - x) * dt * 14` que usaba el prototipo.
        float t = 1f - Mathf.Exp(-cfg.followSharpness * Time.deltaTime);
        float newX = Mathf.Lerp(transform.position.x, targetX, t);
        transform.position = new Vector3(newX, startY, 0f);
    }

    /// El motor de físicas nos avisa cuando algo toca a la nave.
    /// Identificamos QUÉ nos tocó por su Tag (asignado en cada prefab).
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (GameManager.Instance.State != GameState.Playing) return;

        if (other.CompareTag("Meteor"))
        {
            // Con ESCUDO la nave embiste: destruye el meteorito y suma
            // puntos. Sin escudo, el choque termina la partida.
            if (powerUps.IsActive(PowerUpType.Shield))
                other.GetComponent<Meteor>().ShieldRam();
            else
                GameManager.Instance.GameOver();
        }
        else if (other.CompareTag("Item"))
        {
            // Ítem vs jugador → recolección (el ítem suma puntos y se recicla)
            other.GetComponent<CollectibleItem>().Collect();
        }
        else if (other.CompareTag("PowerUp"))
        {
            // Power-up → suma duración a su temporizador (acumulable)
            other.GetComponent<PowerUpItem>().Collect();
        }
    }
}
