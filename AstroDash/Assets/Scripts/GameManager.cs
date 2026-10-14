using System;
using UnityEngine;

/// <summary>
/// Cerebro del juego. Equivalente a las variables globales `state`, `score`,
/// `best` y `elapsed` del prototipo HTML, más las funciones startGame().
///
/// Es un singleton: existe una sola instancia en la escena y cualquier
/// script accede con GameManager.Instance. Los demás sistemas (HUD,
/// spawner, jugador) se enteran de los cambios mediante eventos C#,
/// así el GameManager no necesita conocerlos a ellos.
/// </summary>
public enum GameState { Start, Playing, GameOver }

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [SerializeField] private GameConfig config;
    [SerializeField] private AudioClip gameOverClip;

    public GameConfig Config => config;
    public GameState State { get; private set; } = GameState.Start;
    public int Score { get; private set; }
    public int Best { get; private set; }
    public float Elapsed { get; private set; }

    /// Estrellas recogidas en ESTA partida (el saldo total vive en Progress)
    public int RunStars { get; private set; }

    private float distanceScore;   // acumulador fraccional de la distancia

    /// Dificultad normalizada 0→1 (igual que `difficulty` en el prototipo)
    public float Difficulty01 => Mathf.Clamp01(Elapsed / config.difficultyRamp);

    // Eventos: el HUD y otros sistemas se suscriben para reaccionar
    public event Action<int> OnScoreChanged;
    public event Action<int> OnStarsChanged;
    public event Action<GameState> OnStateChanged;

    private const string BestKey = "astrodash_best";

    private void Awake()
    {
        // Patrón singleton clásico: si ya existe otro, este sobra
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        Best = PlayerPrefs.GetInt(BestKey, 0);   // equivalente a localStorage
        Application.targetFrameRate = 60;        // suaviza y ahorra batería en móvil

        // Modo prueba de la tienda (ver GameConfig.unlockAllForTesting)
        Progress.UnlockAllOverride = config.unlockAllForTesting;
    }

    /// Abandonar desde la PAUSA: guarda el récord si tocaba, limpia el
    /// mundo (todo vuelve a su pool) y regresa al menú principal.
    public void QuitToMenu()
    {
        if (Score > Best)
        {
            Best = Score;
            PlayerPrefs.SetInt(BestKey, Best);
            PlayerPrefs.Save();
        }
        foreach (var pooled in FindObjectsByType<PooledObject>(FindObjectsSortMode.None))
            pooled.Release();
        SetState(GameState.Start);
    }

    private void Update()
    {
        // En GAME OVER tocar la pantalla reinicia. En el menú de inicio ya
        // NO: la partida arranca solo con el botón JUGAR (MenuController) —
        // si no, tocar "Configuración" también empezaría el juego.
        // Tocar SOBRE UI (el botón MENÚ) tampoco cuenta como reintentar.
        if (State == GameState.GameOver && Input.GetMouseButtonDown(0) && !IsPointerOverUi())
            StartGame();

        if (State == GameState.Playing)
        {
            Elapsed += Time.deltaTime;

            // PUNTUACIÓN = DISTANCIA: sube sola con el tiempo, y con el
            // turbo del escudo el mundo avanza más rápido → más puntos.
            // Se acumula en un float y se vuelca al marcador entero.
            distanceScore += config.scorePerSecond
                             * PlayerPowerUps.WorldSpeedMult * Time.deltaTime;
            if (distanceScore >= 1f)
            {
                Score += (int)distanceScore;
                distanceScore -= (int)distanceScore;
                OnScoreChanged?.Invoke(Score);
            }
        }
    }

    private void Start()
    {
        // Aplicar el fondo equipado (comprado en la tienda) al arrancar
        if (Catalog.Instance != null && Camera.main != null)
            Camera.main.backgroundColor =
                Catalog.Instance.GetBackground(Progress.EquippedBackground).cameraColor;
    }

    public void StartGame()
    {
        // Devuelve al pool todos los objetos que quedaron de la partida
        // anterior (balas, meteoritos, ítems) — equivalente a vaciar los
        // arrays en el startGame() del prototipo.
        foreach (var pooled in FindObjectsByType<PooledObject>(FindObjectsSortMode.None))
            pooled.Release();

        Score = 0;
        Elapsed = 0f;
        distanceScore = 0f;
        RunStars = 0;
        SetState(GameState.Playing);
        OnScoreChanged?.Invoke(Score);
        OnStarsChanged?.Invoke(RunStars);
    }

    public void AddScore(int points)
    {
        Score += points;
        OnScoreChanged?.Invoke(Score);
    }

    /// Estrella recogida: ya no da puntos — es MONEDA. Va al saldo
    /// persistente (Progress) y al contador de la partida (HUD).
    public void AddStar()
    {
        RunStars += 1;
        Progress.AddStars(1);
        OnStarsChanged?.Invoke(RunStars);
    }

    /// La llama PlayerController cuando un meteorito toca la nave
    public void GameOver()
    {
        if (State != GameState.Playing) return;

        if (Score > Best)
        {
            Best = Score;
            PlayerPrefs.SetInt(BestKey, Best);
            PlayerPrefs.Save();
        }
#if !UNITY_EDITOR
        Handheld.Vibrate();   // en el teléfono confirma la muerte al tacto
#endif
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PauseMusic();
            AudioManager.Instance.PlaySfx(gameOverClip);
        }
        SetState(GameState.GameOver);
    }

    // ¿El puntero/dedo está sobre un elemento de UI? (para que pulsar el
    // botón MENÚ del game over no cuente también como "toca para reintentar")
    private static bool IsPointerOverUi()
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null) return false;
        return Input.touchCount > 0
            ? es.IsPointerOverGameObject(Input.GetTouch(0).fingerId)
            : es.IsPointerOverGameObject();
    }

    private void SetState(GameState newState)
    {
        State = newState;

        // Cada estado con su pista: partida y menú tienen música propia;
        // en GAME OVER queda el silencio que dejó PauseMusic()
        if (AudioManager.Instance != null)
        {
            if (newState == GameState.Playing)
                AudioManager.Instance.PlayGameplayMusic();
            else if (newState == GameState.Start)
                AudioManager.Instance.PlayMenuMusic();
        }
        if (OnStateChanged == null) return;

        // BLINDAJE: cada suscriptor se invoca por separado. Si uno lanza
        // una excepción, los demás se enteran igual del cambio de estado
        // — sin esto, un solo error dejaba la UI a medias (menú visible
        // sobre la partida, botones de pausa huérfanos, etc.).
        foreach (var handler in OnStateChanged.GetInvocationList())
        {
            try { ((Action<GameState>)handler)(newState); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
