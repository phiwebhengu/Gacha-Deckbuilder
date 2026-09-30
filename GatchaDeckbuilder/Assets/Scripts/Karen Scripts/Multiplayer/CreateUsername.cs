using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using TMPro;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.Core;
using Unity.Services.Lobbies.Models;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class CreateUsername : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField userInputCreate;
    public TMP_InputField userPasswordCreate; //input field for pass word
    public TMP_InputField userInput;
    public TMP_InputField userPassword;
    public Button createAccount;
    public Button signInButton;
    public TextMeshProUGUI statusTextCreate; //Text  for create username
    public TextMeshProUGUI statusTextLogin;//Text for login
    public GameObject signInPanel;
    public GameObject notificationPanel;

    [Header("Scene Settings")]
    public string lobbySceneName = "Lobby"; // Change to your actual lobby scene name

    private bool isInitialized = false;
    private bool isSigningIn = false;

    private Dictionary<GameObject, Coroutine> activeNotifications = new Dictionary<GameObject, Coroutine>();

    async void Start()
    {
        // Disable button until services are ready
        if (signInButton != null) signInButton.interactable = false;

        await UnityServices.InitializeAsync();

        // Enable button once ready
        if (signInButton != null) signInButton.interactable = true;
    }

    public async void OnCreateAccount()
    {
        if (isSigningIn) return;

        string username = userInputCreate.text.Trim();
        string password = userPasswordCreate.text.Trim();

        if (!isValidUsername(username, out string usernameError))
        {
            ShowNotification(statusTextCreate, usernameError);
            return;
        }

        if (!isValidPassword(password, out string passwordError))
        {
            ShowNotification(statusTextCreate, passwordError);
            return;
        }

        isSigningIn = true;
        createAccount.interactable = false;

        // Show "Creating account..." with a longer duration so it doesn't disappear mid-process
        ShowNotification(statusTextCreate, "Creating account...", 5f);

        try
        {
            await AuthenticationService.Instance.SignUpWithUsernamePasswordAsync(username, password);

            Debug.Log("Account created");
            PlayerData.Instance.SetUsername(username);
            PlayerData.Instance.SetPassword(password);

            await SaveCloudData(username, password);

            LoadLobbyScene();
        }
        catch (AuthenticationException e)
        {
            Debug.LogError(e);
            ShowNotification(statusTextCreate, "Account creation failed");
        }
        finally
        {
            isSigningIn = false;
            if (signInButton != null) signInButton.interactable = true;
        }
    }

    public bool isValidUsername(string username, out string error)
    {
        error = "";
        if (string.IsNullOrEmpty(username))
        {
            error = "Please enter a username";
            return false;
        }
        if (username.Length < 3)
        {
            error = "Username must be 3+ characters";
            return false;
        }
        if (username.Length > 20)
        {
            error = "Username must be under 20 characters";
            return false;
        }
        return true;
    }

    public bool isValidPassword(string password, out string error)
    {
        error = "";
        if (string.IsNullOrEmpty(password))
        {
            error = "Please enter a password";
            return false;
        }
        if (password.Length < 8)
        {
            error = "Password must be 8 characters or more";
            return false;
        }

        bool hasUpper = false;
        bool hasLower = false;
        bool hasNum = false;
        bool hasSpecial = false;

        foreach (char c in password)
        {
            if (char.IsUpper(c)) hasUpper = true;
            if (char.IsLower(c)) hasLower = true;
            if (char.IsDigit(c)) hasNum = true;
            if (!char.IsLetterOrDigit(c)) hasSpecial = true;
        }

        if (!hasLower) { error = "Password needs a lowercase letter"; return false; }
        if (!hasUpper) { error = "Password needs an uppercase letter"; return false; }
        if (!hasNum) { error = "Password needs a number"; return false; }
        if (!hasSpecial) { error = "Password needs a special character"; return false; }

        return true;
    }

    public async void OnSignInButtonClicked()
    {
        if (isSigningIn) return;

        string username = userInput.text.Trim();
        string password = userPassword.text.Trim();

        if (string.IsNullOrEmpty(username))
        {
            ShowNotification(statusTextLogin, "Please enter a username");
            return;
        }
        if (string.IsNullOrEmpty(password))
        {
            ShowNotification(statusTextLogin, "Please enter a password");
            return;
        }

        isSigningIn = true;
        if (signInButton != null) signInButton.interactable = false;
        ShowNotification(statusTextLogin, "Signing in...", 5f);

        try
        {
            await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(username, password);

            Debug.Log("Signed in as: " + AuthenticationService.Instance.PlayerId);
            PlayerData.Instance.SetUsername(username);
            PlayerData.Instance.SetPassword(password);

            await LoadCloudData();

            LoadLobbyScene();
        }
        catch (AuthenticationException e)
        {
            Debug.LogError(e);
            ShowNotification(statusTextLogin, "Invalid username or password");
        }
        catch (RequestFailedException e)
        {
            Debug.LogError(e);
            ShowNotification(statusTextLogin, "Invalid username or password");
        }
        finally
        {
            isSigningIn = false;
            if (signInButton != null) signInButton.interactable = true;
        }
    }

    async Task InitializeServices()
    {
        try
        {
            await UnityServices.InitializeAsync();
            isInitialized = true;
            Debug.Log("Unity Services initialized and signed in");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to initialize services: {e.Message}");
            ShowNotification(statusTextLogin, "Connection failed");
        }
    }

    void LoadLobbyScene()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(lobbySceneName);
    }

    public async Task SaveCloudData(string username, string password)
    {
        var data = new Dictionary<string, object>
        {
            {"username", username},
            {"password", password},
            {"wins", PlayerData.Instance.wins},
            {"losses", PlayerData.Instance.losses},
            {"draw", PlayerData.Instance.draw},
            { "lastLogin", System.DateTime.UtcNow.ToString() }
        };

        await CloudSaveService.Instance.Data.ForceSaveAsync(data);
        Debug.Log("Username saved to CloudSave");
    }

    async Task LoadCloudData() 
    {

        var data = await CloudSaveService.Instance.Data.LoadAsync(new HashSet<string> { "wins", "losses", "username", "password", "draw" });

        PlayerData.Instance.setWins(data.ContainsKey("wins") ? float.Parse(data["wins"].ToString()) : 0);
        PlayerData.Instance.setLoss(data.ContainsKey("losses") ? float.Parse(data["losses"].ToString()) : 0);
        PlayerData.Instance.setDraw(data.ContainsKey("draw") ? float.Parse(data["draw"].ToString()) : 0);

        string username = data.ContainsKey("username") ? data["username"].ToString() : "";
        string password = data.ContainsKey("password") ? data["password"].ToString() : "";

        Debug.Log("Username: " + username);
        Debug.Log("Password: " + password);
    }

    public void openSignInPanel() 
    {
        signInPanel.SetActive(true);
    }

    public void closeSignInPanel()
    {
        signInPanel.SetActive(false);
    }
    private void ShowNotification(TextMeshProUGUI statusText, string message, float duration = 2f)
    {
        if (statusText == null) return;

        // Prioritize the notificationPanel if it's assigned in the Inspector, 
        // otherwise fallback to the text object's own GameObject
        GameObject targetObject = notificationPanel != null ? notificationPanel : statusText.gameObject;

        // Cancel any existing hide coroutine for this specific panel to prevent overlapping/early hides
        if (activeNotifications.TryGetValue(targetObject, out Coroutine existingCoroutine))
        {
            StopCoroutine(existingCoroutine);
            activeNotifications.Remove(targetObject);
        }

        // Update the text and activate the panel
        statusText.text = message;
        targetObject.SetActive(true);

        // Start the hide timer, passing only the targetObject and duration
        activeNotifications[targetObject] = StartCoroutine(HideNotificationAfterDelay(targetObject, duration));
    }

    private IEnumerator HideNotificationAfterDelay(GameObject targetObject, float delay)
    {
        yield return new WaitForSeconds(delay);

        // Only hide if the object hasn't been destroyed (e.g., due to a scene change)
        if (targetObject != null)
        {
            targetObject.SetActive(false);
        }

        activeNotifications.Remove(targetObject);
    }
}