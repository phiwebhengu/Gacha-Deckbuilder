using System.Collections.Generic;
using TMPro;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.Services.CloudSave;
using System.Threading.Tasks;
using Unity.VisualScripting;
using Unity.Services.Lobbies.Models;
using System.Linq.Expressions;

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

    [Header("Scene Settings")]
    public string lobbySceneName = "Lobby"; // Change to your actual lobby scene name

    private bool isInitialized = false;
    private bool isSigningIn = false;




    async void Start()
    {
        // Disable button until services are ready
        if (signInButton != null) signInButton.interactable = false;

        await UnityServices.InitializeAsync();

        // Try to load existing username
        // await LoadUsername();

        // Enable button once ready
        if (signInButton != null) signInButton.interactable = true;
    }

    public async void OnCreateAccount()
    {
        if (isSigningIn) return;

        string username = userInputCreate.text.Trim();
        string password = userPasswordCreate.text.Trim(); //Gets rid of spaces

        // Validate username
        if (string.IsNullOrEmpty(username))
        {
            if (statusTextCreate != null) statusTextCreate.text = "Please enter a username";
            return;
        }

        if (username.Length < 3)
        {
            if (statusTextCreate != null) statusTextCreate.text = "Username must be 3+ characters";
            return;
        }

        if (username.Length > 20)
        {
            if (statusTextCreate != null) statusTextCreate.text = "Username must be under 20 characters";
            return;
        }

        //Validate password
        bool hasUpper = false;
        bool hasLower = false;
        bool hasNum = false;
        bool hasSpecial = false;

        if (string.IsNullOrEmpty(password))
        {
            if (statusTextCreate != null) statusTextCreate.text = "Please enter a password";
            return;
        }

        if (password.Length < 8)
        {
            if (statusTextCreate != null) statusTextCreate.text = "Password must be 8 characters or more";
            return;
        }

        if (password.Length > 20)
        {
            if (statusTextCreate != null) statusTextCreate.text = "Password must be under 20 characters";
            return;
        }

        foreach (char c in password)
        {
            if (char.IsLower(c))
            {
                hasLower = true;
            }

            if (char.IsUpper(c))
            {
                hasUpper = true;
            }

            if (char.IsDigit(c))
            {
                hasNum = true;
            }

            if (!char.IsLetterOrDigit(c))
            {
                hasSpecial = true;
            }
        }

        if (!hasLower)
        {
            statusTextCreate.text = "Password needs a lowercase letter";
            return;
        }

        if (!hasUpper)
        {
            statusTextCreate.text = "Password needs an uppercase letter";
            return;
        }

        if (!hasNum)
        {
            statusTextCreate.text = "Password needs a number";
            return;
        }

        if (!hasSpecial)
        {
            statusTextCreate.text = "Password needs a special character";
            return;
        }

        isSigningIn = true;
        createAccount.interactable = false;
        statusTextCreate.text = "Creating account...";

        try
        {
            await AuthenticationService.Instance.SignUpWithUsernamePasswordAsync(username, password);

            Debug.Log("Account created");
            PlayerData.Instance.SetUsername(username);
            PlayerData.Instance.SetPassword(password);

            await SaveCloudData(username, password); //Saves to Cloud Save

            LoadLobbyScene();
        }
        catch (AuthenticationException e)
        {
            Debug.LogError(e);
            statusTextCreate.text = "Account creation failed"; //Triggers if trying to signing into an existing account
        }
        finally
        {
            isSigningIn = false;
            signInButton.interactable = true;
        }
    }

    public bool isValidUsername(string username, out string error) //Seperate the validation to make testing easier
    {
        error = "";
        //Validation tests
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

    public bool isValidPassword(string password, out string error) //Seperate validation for password test cases
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

        if (!hasLower)
        {
            error = "Password needs a lowercase letter";
            return false;
        }

        if (!hasUpper)
        {
            error = "Password needs an uppercase letter";
            return false;
        }

        if (!hasNum)
        {
            error = "Password needs a number";
            return false;
        }

        if (!hasSpecial)
        {
            error = "Password needs a special character";
            return false;
        }

        return true;
    }

    public async void OnSignInButtonClicked()
    {
        // Prevent double-clicks
        if (isSigningIn) return;

        string username = userInput.text.Trim();
        string password = userPassword.text.Trim();

        // Validate username
        if (string.IsNullOrEmpty(username))
        {
            if (statusTextLogin != null) statusTextLogin.text = "Please enter a username";
            return;
        }
        //Validate password
        if (string.IsNullOrEmpty(password))
        {
            if (statusTextLogin != null) statusTextLogin.text = "Please enter a password";
            return;
        }



        // Start sign-in process
        isSigningIn = true;
        if (signInButton != null) signInButton.interactable = false;
        if (statusTextLogin != null) statusTextLogin.text = "Signing in...";

        try
        {
            await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(username, password);

            Debug.Log("Signed in as: " + AuthenticationService.Instance.PlayerId);
            PlayerData.Instance.SetUsername(username);
            PlayerData.Instance.SetPassword(password);

            await LoadCloudData(); // load wins/losses

            LoadLobbyScene();
        }
        catch (AuthenticationException e) //Cateches invalid usernames and password
        {
            Debug.LogError(e);
            statusTextLogin.text = "Invalid username or password";
        }
        catch (RequestFailedException e)
        {
            Debug.LogError(e);
            statusTextLogin.text = "Invalid username or password";
        }
        finally
        {
            isSigningIn = false;
            signInButton.interactable = true;
        }
    }
    async Task InitializeServices() //Loads unity services
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
            if (statusTextLogin != null) statusTextLogin.text = "Connection failed";
        }
    }

    void LoadLobbyScene()
    {
        // Load your lobby scene that contains UIManager + GameNetwork
        UnityEngine.SceneManagement.SceneManager.LoadScene("Lobby");
    }

    public async Task SaveCloudData(string username, string password) //Dictionary that appears in the CLoud Save
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

    async Task LoadCloudData() //Loads all data in CLoud Save
    {

        var data = await CloudSaveService.Instance.Data.LoadAsync(new HashSet<string> { "wins", "losses", "username", "password", "draw" });

        PlayerData.Instance.setWins(data.ContainsKey("wins") ? float.Parse(data["wins"].ToString()) : 0);
        PlayerData.Instance.setLoss(data.ContainsKey("losses") ? float.Parse(data["losses"].ToString()) : 0); // Sets to the cloud
        PlayerData.Instance.setDraw(data.ContainsKey("draw") ? float.Parse(data["draw"].ToString()) : 0);

        string username = data.ContainsKey("username") ? data["username"].ToString() : "";
        string password = data.ContainsKey("password") ? data["password"].ToString() : "";

        Debug.Log("Username: " + username);
        Debug.Log("Password: " + password);
    }

    void ExecuteSceneLoad()
    {
        // Load the lobby scene (additive or single)
        SceneManager.LoadScene(lobbySceneName);
    }

    public void openSignInPanel() //Toggle the sign in panel
    {
        signInPanel.SetActive(true);
    }

    public void closeSignInPanel()
    {
        signInPanel.SetActive(false);
    }

}