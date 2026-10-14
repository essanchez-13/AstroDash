using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PAUSA del juego. Botón "II" en pantalla (solo visible jugando) o la
/// tecla ESC congelan la partida con Time.timeScale = 0 — todo el
/// movimiento usa Time.deltaTime, así que el mundo se detiene entero.
///
/// El menú de pausa ofrece:
///  - RESUME: reanuda (timeScale = 1).
///  - QUIT TO MAIN MENU: restaura el tiempo, guarda el récord si tocaba,
///    limpia el mundo y vuelve al menú principal (GameManager.QuitToMenu).
/// </summary>
public class PauseController : MonoBehaviour
{
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private Button pauseButton;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button quitButton;

    public static bool IsPaused { get; private set; }

    private void Start()
    {
        pauseButton.onClick.AddListener(Pause);
        resumeButton.onClick.AddListener(Resume);
        quitButton.onClick.AddListener(QuitToMenu);

        GameManager.Instance.OnStateChanged += state =>
        {
            pauseButton.gameObject.SetActive(state == GameState.Playing);
            // Red de seguridad: nunca dejar el tiempo congelado fuera de partida
            if (state != GameState.Playing && IsPaused) Resume();
        };
        pauseButton.gameObject.SetActive(false);
        pausePanel.SetActive(false);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) &&
            GameManager.Instance.State == GameState.Playing)
        {
            if (IsPaused) Resume();
            else Pause();
        }
    }

    private void Pause()
    {
        if (GameManager.Instance.State != GameState.Playing || IsPaused) return;
        IsPaused = true;
        Time.timeScale = 0f;
        pausePanel.SetActive(true);
        if (AudioManager.Instance != null)
            AudioManager.Instance.PauseMusic();   // pausa = silencio
    }

    private void Resume()
    {
        IsPaused = false;
        Time.timeScale = 1f;
        pausePanel.SetActive(false);
        if (AudioManager.Instance != null)
            AudioManager.Instance.ResumeMusic();  // sigue donde iba, con fade
    }

    private void QuitToMenu()
    {
        Resume();                          // SIEMPRE restaurar el tiempo antes
        GameManager.Instance.QuitToMenu();
    }
}
