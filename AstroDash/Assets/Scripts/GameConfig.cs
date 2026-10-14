using UnityEngine;

/// <summary>
/// Equivalente del objeto CONFIG del prototipo HTML.
/// Es un ScriptableObject: un "archivo de datos" que vive en el proyecto
/// (Assets > Create > AstroDash > GameConfig) y se ajusta desde el Inspector
/// sin tocar código. Ideal para iterar el game feel — y más adelante estos
/// mismos valores se pueden sobreescribir con Firebase Remote Config.
///
/// NOTA DE ESCALA: el prototipo usaba un mundo de 420x740 "píxeles lógicos".
/// En Unity trabajamos en unidades de mundo: dividimos todo entre 50.
/// El mundo pasa a medir ~8.4 x 14.8 unidades (cámara ortográfica size 7.4).
/// </summary>
[CreateAssetMenu(fileName = "GameConfig", menuName = "AstroDash/GameConfig")]
public class GameConfig : ScriptableObject
{
    [Header("Jugador")]
    [Tooltip("Velocidad horizontal con teclado (unidades/seg)")]
    public float playerSpeed = 10.4f;
    [Tooltip("Qué tan rápido persigue la nave al dedo (mayor = más pegada)")]
    public float followSharpness = 14f;

    [Header("Disparo automático")]
    public float fireInterval = 0.28f;   // segundos entre disparos
    public float bulletSpeed = 12.4f;

    [Header("Meteoritos")]
    public float meteorSpawnStart = 1.0f;  // intervalo inicial entre spawns (seg)
    public float meteorSpawnMin = 0.35f;   // intervalo con dificultad máxima
    public float meteorSpeedMin = 2.4f;
    public float meteorSpeedMax = 4.8f;
    public float meteorDriftMax = 0.8f;    // deriva lateral máxima

    [Header("Clases de meteorito (sprites de asteroides.png)")]
    [Tooltip("Probabilidad de que salga un meteorito pequeño (1 impacto)")]
    [Range(0f, 1f)] public float smallChance = 0.45f;
    [Tooltip("De los grandes, fracción que son ROJOS (2 impactos)")]
    [Range(0f, 1f)] public float redChance = 0.4f;
    [Tooltip("Diámetro en unidades de mundo de cada clase")]
    public float smallSizeMin = 0.5f;
    public float smallSizeMax = 0.75f;
    public float bigSizeMin = 0.9f;
    public float bigSizeMax = 1.25f;

    [Header("Ítems galácticos")]
    public float itemSpawnMin = 2.2f;   // aparece cada 2.2–4 segundos
    public float itemSpawnMax = 4.0f;
    public float itemFallSpeed = 2.6f;
    public float itemSwayAmplitude = 0.6f;  // amplitud del vaivén lateral
    public float itemSwayFrequency = 2.5f;

    [Header("Puntuación")]
    [Tooltip("La puntuación es DISTANCIA: sube sola con el tiempo (y más rápido con el turbo del escudo)")]
    public float scorePerSecond = 10f;
    public int scoreMeteor = 10;
    // (las estrellas ya no dan puntos: son MONEDA — ver Progress.cs)

    [Header("Dificultad")]
    [Tooltip("Segundos de partida para alcanzar la dificultad máxima")]
    public float difficultyRamp = 60f;
    [Tooltip("Multiplicador extra de velocidad de meteoritos a dificultad máxima")]
    public float meteorSpeedRamp = 0.6f;

    [Header("Power-ups (acumulables)")]
    [Tooltip("Segundos que añade cada recogida al contador de su tipo")]
    public float powerUpDuration = 7f;
    public float powerUpSpawnMin = 8f;    // aparece uno cada 8–14 segundos
    public float powerUpSpawnMax = 14f;
    public float powerUpFallSpeed = 3f;
    [Tooltip("Diámetro del icono de power-up en unidades de mundo")]
    public float powerUpSize = 0.55f;
    [Tooltip("El disparo rápido multiplica el intervalo (0.5 = doble cadencia)")]
    public float rapidRateMult = 0.5f;
    [Tooltip("Separación lateral de cada bala en el disparo doble")]
    public float doubleShotOffset = 0.18f;
    [Tooltip("Radio de atracción del imán (unidades de mundo)")]
    public float magnetRadius = 3f;
    [Tooltip("Velocidad a la que el imán arrastra los ítems")]
    public float magnetPull = 14f;

    [Header("Escudo = turbo hacia adelante")]
    [Tooltip("El mundo (meteoritos, ítems, cápsulas) baja más rápido con el escudo")]
    public float shieldWorldMult = 1.4f;
    [Tooltip("Las estrellas del fondo bajan aún más rápido (sensación de velocidad)")]
    public float shieldStarsMult = 2.2f;
    [Tooltip("Últimos segundos en los que el aura parpadea antes de acabarse")]
    public float shieldWarnTime = 3f;

    [Header("Power-up de Potencia (bala grande)")]
    [Tooltip("Escala visual y de collider de la bala grande")]
    public float bigBulletScale = 2.2f;
    public int bigBulletDamage = 2;

    [Header("Meteorito portador (suelta un power-up al romperse)")]
    public float carrierSpawnMin = 12f;   // aparece uno cada 12–20 segundos
    public float carrierSpawnMax = 20f;
    public int carrierHp = 3;
    [Tooltip("Diámetro del portador en unidades de mundo (mayor que los grandes)")]
    public float carrierScale = 1.45f;

    [Header("Desarrollo")]
    [Tooltip("MODO PRUEBA: todo el catálogo desbloqueado sin comprar. " +
             "¡DESACTIVAR antes de publicar!")]
    public bool unlockAllForTesting = false;
}
