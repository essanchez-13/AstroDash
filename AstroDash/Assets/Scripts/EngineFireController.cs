using UnityEngine;

/// <summary>
/// Fuego del motor de la nave (ParticleSystem en la base). Con el
/// ESCUDO activo (turbo hacia adelante) las partículas salen más
/// rápidas, más grandes y más largas; al acabar el escudo la llama
/// vuelve SUAVEMENTE a su estado normal (transición interpolada, sin
/// saltos). También se apaga fuera de la partida.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class EngineFireController : MonoBehaviour
{
    [Header("Multiplicadores con el turbo del escudo")]
    [SerializeField] private float boostSpeed = 2.2f;
    [SerializeField] private float boostSize = 1.5f;
    [SerializeField] private float boostLifetime = 1.7f;   // llama más larga
    [SerializeField] private float boostRate = 1.8f;
    [SerializeField] private float blendSpeed = 6f;        // rapidez de la transición

    private ParticleSystem ps;
    private ParticleSystem.MinMaxCurve baseSpeed, baseSize, baseLife;
    private float baseRate;
    private float boost;   // 0 = normal, 1 = turbo

    private void Awake()
    {
        ps = GetComponent<ParticleSystem>();
        // Guardar los valores del prefab para poder volver a ellos
        var main = ps.main;
        baseSpeed = main.startSpeed;
        baseSize = main.startSize;
        baseLife = main.startLifetime;
        baseRate = ps.emission.rateOverTime.constant;
    }

    private void Start()
    {
        // La llama simula en espacio de MUNDO: cuando la nave se
        // teletransporta (entrada de partida, volver al menú), las
        // partículas ya emitidas se quedarían flotando donde estaba —
        // "fuego fantasma". Se limpian en cada cambio de estado.
        GameManager.Instance.OnStateChanged += _ => ps.Clear();
    }

    private void Update()
    {
        bool playing = GameManager.Instance.State == GameState.Playing;

        // El fuego solo arde durante la partida
        if (playing && !ps.isPlaying) ps.Play();
        else if (!playing && ps.isPlaying) ps.Stop();
        if (!playing) return;

        var pp = PlayerPowerUps.Instance;
        float target = (pp != null && pp.IsActive(PowerUpType.Shield)) ? 1f : 0f;
        boost = Mathf.MoveTowards(boost, target, blendSpeed * Time.deltaTime);

        var main = ps.main;
        main.startSpeed    = Scale(baseSpeed, Mathf.Lerp(1f, boostSpeed, boost));
        main.startSize     = Scale(baseSize, Mathf.Lerp(1f, boostSize, boost));
        main.startLifetime = Scale(baseLife, Mathf.Lerp(1f, boostLifetime, boost));
        var emission = ps.emission;
        emission.rateOverTime = baseRate * Mathf.Lerp(1f, boostRate, boost);
    }

    // Escala una curva de dos constantes (min/max) conservando su rango
    private static ParticleSystem.MinMaxCurve Scale(ParticleSystem.MinMaxCurve curve, float k)
        => new ParticleSystem.MinMaxCurve(curve.constantMin * k, curve.constantMax * k);
}
