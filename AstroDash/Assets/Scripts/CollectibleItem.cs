using UnityEngine;

/// <summary>
/// Ítem galáctico (estrella dorada). Cae despacio con un vaivén
/// sinusoidal — la misma fórmula del prototipo: x = baseX + sin(t)·amplitud.
/// Recogerlo suma puntos; nunca daña al jugador.
///
/// Setup del prefab: SpriteRenderer (rombo/diamante dorado #FBBF24),
/// tag "Item", CircleCollider2D con "Is Trigger", Rigidbody2D Kinematic.
/// </summary>
public class CollectibleItem : MonoBehaviour
{
    [Tooltip("Sonido al recoger la estrella (lo cablea la Fase 28)")]
    [SerializeField] private AudioClip collectClip;

    private float baseX;   // centro del vaivén
    private float t;       // tiempo propio (fase del seno)

    // OnEnable se ejecuta cada vez que el pool reactiva el objeto,
    // así cada aparición arranca con una fase aleatoria distinta
    private void OnEnable()
    {
        baseX = transform.position.x;
        t = Random.Range(0f, Mathf.PI * 2f);
        magnetized = false;   // los objetos del pool se reciclan: resetear
    }

    private bool magnetized;   // ya "capturado" por el imán

    private void Update()
    {
        var cfg = GameManager.Instance.Config;
        t += Time.deltaTime;
        transform.Rotate(0f, 0f, 90f * Time.deltaTime);   // giro decorativo

        /* IMÁN (corregido): al entrar en el radio, el ítem queda
           "imantado" y vuela DIRECTO a la nave SIN vaivén — el vaivén
           era lo que lo desviaba a los lados justo antes de recogerlo.
           Si el imán se acaba antes de llegar, retoma la caída normal
           desde donde esté, sin saltos. */
        var pp = PlayerPowerUps.Instance;
        if (pp != null && pp.IsActive(PowerUpType.Magnet))
        {
            Vector3 toPlayer = pp.transform.position - transform.position;
            float dist = toPlayer.magnitude;
            if (!magnetized && dist < cfg.magnetRadius) magnetized = true;
            if (magnetized && dist > 0.01f)
            {
                float step = Mathf.Min(cfg.magnetPull * Time.deltaTime, dist);
                transform.position += toPlayer / dist * step;
                return;   // este frame ya voló hacia la nave
            }
        }
        else if (magnetized)
        {
            magnetized = false;
            // reancla el vaivén a la posición actual para evitar un salto
            baseX = transform.position.x - Mathf.Sin(t * cfg.itemSwayFrequency) * cfg.itemSwayAmplitude;
        }

        // Caída normal (con el turbo del escudo el mundo baja más rápido)
        float y = transform.position.y
                  - cfg.itemFallSpeed * PlayerPowerUps.WorldSpeedMult * Time.deltaTime;
        float x = baseX + Mathf.Sin(t * cfg.itemSwayFrequency) * cfg.itemSwayAmplitude;
        transform.position = new Vector3(x, y, 0f);

        // Salió por abajo sin ser recogido → reciclar
        if (transform.position.y < -ScreenBounds.HalfHeight - 1f)
            GetComponent<PooledObject>().Release();
    }

    /// La llama PlayerController al tocar el ítem. Las estrellas ya no
    /// dan puntos: son MONEDA para la tienda (saldo persistente).
    public void Collect()
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySfx(collectClip, 0.8f);
        GameManager.Instance.AddStar();
        GetComponent<PooledObject>().Release();
    }
}
