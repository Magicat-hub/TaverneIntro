using UnityEngine;

/// <summary>
/// Point central pour changer de scène ou quitter le jeu.
/// Les méthodes sont publiques et sans paramètre (ou à paramètre simple)
/// pour pouvoir être branchées directement sur un bouton UI (OnClick).
///
/// Note : ce script s'appelle "SceneManager", comme la classe Unity
/// UnityEngine.SceneManagement.SceneManager. À l'intérieur de ce fichier,
/// on doit donc toujours écrire le nom complet ("UnityEngine.SceneManagement.SceneManager")
/// pour ne pas s'appeler soi-même par erreur.
/// </summary>
public class LocalSceneManager : MonoBehaviour
{
    [Tooltip("Nom de la scène à charger par défaut si LoadNextScene() est appelé sans argument (ex. bouton \"Suivant\").")]
    public string nextSceneName;

    [Tooltip("Nom de la scène menu/titre, pour un bouton \"Retour au menu\".")]
    public string mainMenuSceneName;

    /// <summary>Charge une scène par son nom (doit être ajoutée dans Build Settings).</summary>
    public void LoadScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning("[SceneManager] LoadScene appelé avec un nom de scène vide.");
            return;
        }

        UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
    }

    /// <summary>Charge une scène par son index dans Build Settings.</summary>
    public void LoadScene(int buildIndex)
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(buildIndex);
    }

    /// <summary>Recharge la scène actuelle (utile pour un bouton "Réessayer").</summary>
    public void ReloadCurrentScene()
    {
        var current = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        UnityEngine.SceneManagement.SceneManager.LoadScene(current.buildIndex);
    }

    /// <summary>Charge la scène définie dans nextSceneName (bouton "Suivant" générique).</summary>
    public void LoadNextScene()
    {
        LoadScene(nextSceneName);
    }

    /// <summary>Charge la scène définie dans mainMenuSceneName (bouton "Menu").</summary>
    public void LoadMainMenu()
    {
        LoadScene(mainMenuSceneName);
    }

    /// <summary>Quitte l'application. Dans l'Éditeur, arrête simplement le mode Play.</summary>
    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
