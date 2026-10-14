using UnityEngine;

/// <summary>
/// GENERACIÓN ALEATORIA DE OBJETOS — equivalente a los bloques
/// `meteorTimer` / `itemTimer` + spawnMeteor() / spawnItem() del prototipo.
///
/// - Meteoritos: el intervalo entre apariciones se acorta con la
///   dificultad (interpolación lineal de spawnStart a spawnMin).
/// - Ítems: aparecen a intervalos aleatorios dentro de un rango fijo.
/// Ambos salen justo por encima del borde superior, en una X aleatoria.
///
/// Setup: GameObject vacío "Spawner" con este componente y dos hijos
/// con SimplePool (uno para el prefab Meteor, otro para el prefab Item).
/// </summary>
public class Spawner : MonoBehaviour
{
    [SerializeField] private SimplePool meteorPool;
    [SerializeField] private SimplePool itemPool;
    [SerializeField] private SimplePool powerUpPool;

    private float meteorTimer;
    private float itemTimer;
    private float powerUpTimer;
    private float carrierTimer;

    private void Start()
    {
        // Al empezar cada partida, reiniciar los temporizadores
        GameManager.Instance.OnStateChanged += state =>
        {
            if (state == GameState.Playing)
            {
                meteorTimer = 0.6f;   // primer meteorito casi de inmediato
                itemTimer = RandomItemInterval();
                powerUpTimer = RandomPowerUpInterval();
                carrierTimer = RandomCarrierInterval();
            }
        };
    }

    // El portador avisa al morir y aquí soltamos su cápsula en ese punto
    private void OnEnable() => Meteor.OnCarrierDestroyed += DropPowerUp;
    private void OnDisable() => Meteor.OnCarrierDestroyed -= DropPowerUp;

    private void DropPowerUp(Vector3 position, PowerUpType type)
    {
        powerUpPool.Get(position).GetComponent<PowerUpItem>().Setup(type);
    }

    private void Update()
    {
        var gm = GameManager.Instance;
        if (gm.State != GameState.Playing) return;
        var cfg = gm.Config;

        // Con el turbo del escudo los relojes de aparición también corren
        // más rápido, para mantener la densidad de objetos en pantalla
        float dt = Time.deltaTime * PlayerPowerUps.WorldSpeedMult;

        // --- Meteoritos: el intervalo se encoge según la dificultad ---
        meteorTimer -= dt;
        if (meteorTimer <= 0f)
        {
            meteorTimer = Mathf.Lerp(cfg.meteorSpawnStart, cfg.meteorSpawnMin, gm.Difficulty01);
            SpawnMeteor(cfg, gm.Difficulty01);
        }

        // --- Portador: uno cada 12–20 s, con un power-up dentro ---
        carrierTimer -= dt;
        if (carrierTimer <= 0f)
        {
            carrierTimer = RandomCarrierInterval();
            SpawnCarrier(cfg);
        }

        // --- Ítems: intervalo aleatorio fijo ---
        itemTimer -= dt;
        if (itemTimer <= 0f)
        {
            itemTimer = RandomItemInterval();
            SpawnItem();
        }

        // --- Power-ups: uno cada 8–14 s, con tipo aleatorio ---
        powerUpTimer -= dt;
        if (powerUpTimer <= 0f)
        {
            powerUpTimer = RandomPowerUpInterval();
            SpawnPowerUp();
        }
    }

    private void SpawnCarrier(GameConfig cfg)
    {
        float x = Random.Range(-ScreenBounds.HalfWidth + 0.8f, ScreenBounds.HalfWidth - 0.8f);
        var go = meteorPool.Get(new Vector3(x, ScreenBounds.HalfHeight + 1.5f, 0f));
        go.GetComponent<Meteor>().SetupCarrier(cfg);
    }

    private float RandomCarrierInterval()
    {
        var cfg = GameManager.Instance.Config;
        return Random.Range(cfg.carrierSpawnMin, cfg.carrierSpawnMax);
    }

    private void SpawnPowerUp()
    {
        float margin = 0.8f;
        float x = Random.Range(-ScreenBounds.HalfWidth + margin, ScreenBounds.HalfWidth - margin);
        var go = powerUpPool.Get(new Vector3(x, ScreenBounds.HalfHeight + 0.8f, 0f));

        // Tipo al azar entre los 4 del enum PowerUpType
        go.GetComponent<PowerUpItem>().Setup((PowerUpType)Random.Range(0, 4));
    }

    private float RandomPowerUpInterval()
    {
        var cfg = GameManager.Instance.Config;
        return Random.Range(cfg.powerUpSpawnMin, cfg.powerUpSpawnMax);
    }

    private void SpawnMeteor(GameConfig cfg, float difficulty)
    {
        // X aleatoria dentro de la pantalla, Y justo sobre el borde superior
        float x = Random.Range(-ScreenBounds.HalfWidth + 0.6f, ScreenBounds.HalfWidth - 0.6f);
        var go = meteorPool.Get(new Vector3(x, ScreenBounds.HalfHeight + 1.5f, 0f));

        // Cada meteorito recibe tamaño/velocidad/giro aleatorios nuevos
        go.GetComponent<Meteor>().Setup(cfg, difficulty);
    }

    private void SpawnItem()
    {
        float margin = 1.0f;   // margen para que el vaivén no lo saque de pantalla
        float x = Random.Range(-ScreenBounds.HalfWidth + margin, ScreenBounds.HalfWidth - margin);
        itemPool.Get(new Vector3(x, ScreenBounds.HalfHeight + 0.8f, 0f));
    }

    private float RandomItemInterval()
    {
        var cfg = GameManager.Instance.Config;
        return Random.Range(cfg.itemSpawnMin, cfg.itemSpawnMax);
    }
}
