using UnityEngine;

/// <summary>
/// (Opcional pero recomendado) Fondo de estrellas con parallax simple,
/// equivalente al array `bgStars` del prototipo: puntos que caen a
/// velocidades distintas y reaparecen arriba al salir por abajo.
///
/// Setup: GameObject vacío "Starfield" con este componente. Asigna
/// como prefab un sprite de círculo blanco diminuto (escala ~0.05,
/// color blanco con alpha ~0.35). Las estrellas se generan solas.
/// </summary>
public class Starfield : MonoBehaviour
{
    [SerializeField] private GameObject starPrefab;
    [SerializeField] private int count = 60;
    [SerializeField] private float minSpeed = 0.3f;
    [SerializeField] private float maxSpeed = 1.0f;

    private Transform[] stars;
    private float[] speeds;

    private void Start()
    {
        stars = new Transform[count];
        speeds = new float[count];

        for (int i = 0; i < count; i++)
        {
            var star = Instantiate(starPrefab, RandomPosition(false), Quaternion.identity, transform);
            // Tamaño aleatorio: las "cercanas" son más grandes y rápidas
            float depth = Random.Range(0.4f, 1f);
            star.transform.localScale = Vector3.one * 0.05f * (0.5f + depth);
            speeds[i] = Mathf.Lerp(minSpeed, maxSpeed, depth);
            stars[i] = star.transform;
        }
    }

    private void Update()
    {
        // Con el escudo del jugador, el fondo pasa mucho más rápido:
        // es la principal señal visual del "turbo hacia adelante"
        float mult = 1f;
        var pp = PlayerPowerUps.Instance;
        if (pp != null && GameManager.Instance.State == GameState.Playing
            && pp.IsActive(PowerUpType.Shield))
            mult = GameManager.Instance.Config.shieldStarsMult;

        float bottom = -ScreenBounds.HalfHeight - 0.5f;
        for (int i = 0; i < count; i++)
        {
            stars[i].position += Vector3.down * speeds[i] * mult * Time.deltaTime;
            if (stars[i].position.y < bottom)
                stars[i].position = RandomPosition(true);   // reaparece arriba
        }
    }

    private Vector3 RandomPosition(bool atTop)
    {
        float x = Random.Range(-ScreenBounds.HalfWidth, ScreenBounds.HalfWidth);
        float y = atTop
            ? ScreenBounds.HalfHeight + 0.5f
            : Random.Range(-ScreenBounds.HalfHeight, ScreenBounds.HalfHeight);
        return new Vector3(x, y, 0f);
    }
}
