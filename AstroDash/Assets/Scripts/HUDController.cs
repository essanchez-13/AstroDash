using TMPro;
using UnityEngine;

/// <summary>
/// UI minimalista con los mismos tres estados del prototipo:
/// - Jugando: solo el marcador, grande, arriba al centro.
/// - Inicio: título + instrucciones + "toca para jugar".
/// - Game over: puntuación final + récord + "toca para reintentar".
///
/// El HUD no sabe nada de la lógica: solo escucha los eventos del
/// GameManager y muestra/oculta paneles. Esa separación permite
/// rediseñar la UI sin tocar el gameplay.
///
/// Setup: Canvas (Screen Space - Overlay, Scale With Screen Size
/// 1080x1920) con este componente, y dentro: ScoreText (TMP),
/// StartPanel y GameOverPanel (con sus textos TMP como hijos).
/// </summary>
public class HUDController : MonoBehaviour
{
    [Header("Jugando")]
    [SerializeField] private TextMeshProUGUI scoreText;
    [Tooltip("Línea bajo el marcador con los power-ups activos y sus segundos")]
    [SerializeField] private TextMeshProUGUI powerUpText;
    [Tooltip("Grupo icono+número de estrellas recogidas en la partida")]
    [SerializeField] private GameObject starsGroup;
    [SerializeField] private TextMeshProUGUI starsText;

    [Header("Pantalla de inicio")]
    [SerializeField] private GameObject startPanel;
    [SerializeField] private TextMeshProUGUI startBestText;

    [Header("Pantalla de game over")]
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TextMeshProUGUI finalScoreText;
    [SerializeField] private TextMeshProUGUI gameOverBestText;
    [Tooltip("Estrellas ganadas en la partida (\"+N\")")]
    [SerializeField] private TextMeshProUGUI gameOverStarsText;
    [Tooltip("Botón para volver al menú sin reintentar")]
    [SerializeField] private UnityEngine.UI.Button gameOverMenuButton;

    private void Start()
    {
        var gm = GameManager.Instance;
        gm.OnScoreChanged += UpdateScore;
        gm.OnStateChanged += UpdatePanels;
        gm.OnStarsChanged += stars =>
        {
            if (starsText != null) starsText.text = stars.ToString();
        };

        if (gameOverMenuButton != null)
            gameOverMenuButton.onClick.AddListener(() => gm.QuitToMenu());

        // Los textos con formato ("Récord: N") se rehacen al cambiar idioma.
        // Loc.OnChanged es ESTÁTICO: hay que darse de baja al morir o la
        // suscripción vieja sobrevive a la escena y revienta con objetos
        // destruidos (MissingReferenceException al cambiar idioma).
        locHandler = () => UpdatePanels(GameManager.Instance.State);
        Loc.OnChanged += locHandler;

        // Estado inicial: menú de inicio
        UpdatePanels(GameState.Start);
    }

    private System.Action locHandler;

    private void OnDestroy()
    {
        if (locHandler != null) Loc.OnChanged -= locHandler;
    }

    private void UpdateScore(int score) => scoreText.text = score.ToString();

    // Colores (hex) de cada power-up, indexados por PowerUpType.
    // Ya no hay letra identificadora: el COLOR del número es lo que
    // dice de qué power-up se trata (coincide con su icono).
    private static readonly string[] PowerColors = { "#22D3EE", "#A78BFA", "#F472B6", "#FB923C", "#4ADE80" };

    /// La línea de power-ups cambia cada frame (cuenta atrás), así que se
    /// refresca aquí en vez de por eventos. Usa rich text de TMP para dar
    /// a cada contador el color de su tipo.
    private void Update()
    {
        if (powerUpText == null) return;

        var pp = PlayerPowerUps.Instance;
        if (pp == null || GameManager.Instance.State != GameState.Playing)
        {
            powerUpText.text = "";
            return;
        }

        var parts = new System.Collections.Generic.List<string>(5);
        for (int i = 0; i < 5; i++)
        {
            var type = (PowerUpType)i;
            if (pp.IsActive(type))
                parts.Add($"<color={PowerColors[i]}>{Mathf.CeilToInt(pp.Remaining(type))}</color>");
        }
        powerUpText.text = string.Join("   ", parts);
    }

    private void UpdatePanels(GameState state)
    {
        var gm = GameManager.Instance;

        startPanel.SetActive(state == GameState.Start);
        gameOverPanel.SetActive(state == GameState.GameOver);
        scoreText.gameObject.SetActive(state != GameState.Start);
        if (starsGroup != null)
            starsGroup.SetActive(state == GameState.Playing);

        if (state == GameState.Start && startBestText != null)
            startBestText.text = gm.Best > 0 ? $"{Loc.T("best")}: {gm.Best}" : "";

        if (state == GameState.GameOver)
        {
            finalScoreText.text = gm.Score.ToString();
            gameOverBestText.text = $"{Loc.T("best")}: {gm.Best}";
            if (gameOverStarsText != null)
                gameOverStarsText.text = $"+{gm.RunStars}";
        }
    }
}
