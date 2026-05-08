using System.Collections;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Safely leaves Photon, reloads the scene, and ensures the main menu shows.
/// Uses DontDestroyOnLoad so it survives the player object being destroyed.
/// </summary>
public class ReturnToMenuHelper : MonoBehaviour
{
    private void Start()
    {
        StartCoroutine(LeaveAndReload());
    }

    private IEnumerator LeaveAndReload()
    {
        if (PhotonNetwork.InRoom)
        {
            PhotonNetwork.LeaveRoom();
            float t = 0f;
            while (PhotonNetwork.InRoom && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
        }

        if (PhotonNetwork.IsConnected)
        {
            PhotonNetwork.Disconnect();
            float t = 0f;
            while (PhotonNetwork.IsConnected && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
        }

        yield return null;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        Destroy(gameObject);
    }
}
