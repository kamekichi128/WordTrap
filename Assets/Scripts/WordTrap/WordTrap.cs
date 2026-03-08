using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using EasyLocalLLM.LLM.Core;
using EasyLocalLLM.LLM.Factory;
using EasyLocalLLM.LLM.Ollama;
using EasyLocalLLM.LLM.WebGL;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Shiritori
{
    public class WordTrap : MonoBehaviour
    {
        [Header("UI Components")]
        [SerializeField] private TMP_Text wordLabel;
        [SerializeField] private TMP_InputField textInput;
        [SerializeField] private Button sendButton;
        [SerializeField] private Button surrenderButton;
        [SerializeField] private TMP_Text logLabel;

        [Header("WebGL (wllama)")]
        [SerializeField] private string webglModelRelativePath = "https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct-GGUF/resolve/main/qwen2.5-0.5b-instruct-q4_k_m.gguf";
        [SerializeField] private string webglWasmBasePath = "llama-wasm";
        [SerializeField] private bool useWebGpu = true;
        [SerializeField] private int contextSize = 2048;

        [Header("Editor / Standalone fallback (Ollama)")]
        [SerializeField] private string ollamaServerUrl = "http://localhost:11434";
        [SerializeField] private string ollamaModelName = "mistral";

        [Header("Dictionary")]
        [SerializeField] private string dictionaryCsvResourcePath = "WordTrap/dictionary";

        [Header("Model Loading")]
        [SerializeField] private float loadingTimeoutSeconds = 300f;

        private IChatLLMClient client;
        private bool isGameActive;
        private bool isBusy;
        private bool isModelLoaded;

        private string currentSecretWord = string.Empty;
        private string currentSecretWordNormalized = string.Empty;

        private readonly List<string> dictionaryWords = new List<string>();
        private readonly System.Random random = new System.Random();
        private readonly StringBuilder logBuilder = new StringBuilder();

        private void Start()
        {
            InitializeClient();
            
            if (!LoadDictionaryFromCsv())
            {
                Debug.LogError("[WordTrap] 辞書CSVの読み込みに失敗しました。");
            }

            RegisterUiEvents();
            StartCoroutine(LoadModelAndStartGame());
        }

        private void OnDestroy()
        {
            UnregisterUiEvents();
        }

        private IEnumerator LoadModelAndStartGame()
        {
            isModelLoaded = false;
            SetControlsInteractable(false);
            
            UpdateWordLabel("モデル読み込み中...");
            AddLog("モデルの読み込みを開始しています...");

            var modelName = GetModelName();
            var loadSucceeded = false;

            yield return client.LoadModelRunnable(
                modelName,
                loadingTimeoutSeconds,
                progress =>
                {
                    var progressPercent = Mathf.RoundToInt((float)progress.Progress * 100);
                    UpdateWordLabel($"モデル読み込み中... {progressPercent}%");
                    logBuilder.Clear();
                    AddLog($"[{progressPercent}%] {progress.Message}");

                    if (progress.IsCompleted)
                    {
                        if (progress.IsSuccessed)
                        {
                            loadSucceeded = true;
                            AddLog("モデルの読み込みが完了しました。");
                        }
                        else
                        {
                            AddLog($"モデルの読み込みに失敗: {progress.Message}");
                        }
                    }
                }
            );

            if (loadSucceeded)
            {
                isModelLoaded = true;
                StartNewGame();
            }
            else
            {
                UpdateWordLabel("モデル読み込み失敗");
                AddLog("ゲームを開始できません。");
                
                if (surrenderButton != null)
                {
                    var buttonText = surrenderButton.GetComponentInChildren<TMP_Text>();
                    if (buttonText != null)
                    {
                        buttonText.text = "再試行";
                    }
                    surrenderButton.onClick.RemoveAllListeners();
                    surrenderButton.onClick.AddListener(() => StartCoroutine(LoadModelAndStartGame()));
                    surrenderButton.interactable = true;
                }
            }
        }

        private string GetModelName()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return System.IO.Path.GetFileNameWithoutExtension(webglModelRelativePath) ?? "webgl-model";
#else
            return ollamaModelName;
#endif
        }

        private void InitializeClient()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
    var modelUrl = ResolveWebGlModelUrl(webglModelRelativePath);
    var wasmBaseUrl = ResolveWebGlWasmBaseUrl(webglWasmBasePath);

    Debug.Log($"[WordTrap] Application.absoluteURL: {Application.absoluteURL}");
    Debug.Log($"[WordTrap] Application.streamingAssetsPath: {Application.streamingAssetsPath}");
    Debug.Log($"[WordTrap] Resolved ModelUrl: {modelUrl}");
    Debug.Log($"[WordTrap] Resolved WasmBaseUrl: {wasmBaseUrl}");

    var webglConfig = new WllamaConfig
    {
        ModelUrl = modelUrl,
        ContextSize = contextSize,
        UseWebGpu = useWebGpu,
        WasmBaseUrl = wasmBaseUrl,
        DebugMode = true,
        InitTimeoutSeconds = 600f
    };

    client = LLMClientFactory.CreateWllamaClient(webglConfig);
#else
    var ollamaConfig = new OllamaConfig
    {
        ServerUrl = ollamaServerUrl,
        DefaultModelName = ollamaModelName,
        AutoStartServer = false,
        DebugMode = true,
        MaxRetries = 2,
        RetryDelaySeconds = 1.0f
    };

    client = LLMClientFactory.CreateOllamaClient(ollamaConfig);
#endif
        }

        private static string ResolveWebGlModelUrl(string configuredPath)
        {
            return CombineWithStreamingAssetsPath(configuredPath, "EasyLocalLLM/models/qwen2.5-0.5b-instruct-q4_k_m.gguf");
        }

        private static string ResolveWebGlWasmBaseUrl(string configuredPath)
        {
            var relativePath = string.IsNullOrWhiteSpace(configuredPath) ? "llama-wasm" : configuredPath.Trim();

            if (relativePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return relativePath;
            }

            // Unity Playでは StreamingAssets への相対パスを使用
            if (relativePath.StartsWith("StreamingAssets/", StringComparison.OrdinalIgnoreCase))
            {
                relativePath = relativePath.Substring("StreamingAssets/".Length);
            }

            // Unity Playの場合、絶対URLを生成
            var baseUrl = Application.absoluteURL;
            if (!string.IsNullOrEmpty(baseUrl))
            {
                // URLの末尾から不要な部分を削除
                var uri = new Uri(baseUrl);
                var basePath = uri.GetLeftPart(UriPartial.Authority) + "/StreamingAssets";
                return basePath + "/" + relativePath.TrimStart('/');
            }

            // フォールバック
            var streamingPath = (Application.streamingAssetsPath ?? "StreamingAssets").TrimEnd('/');
            return streamingPath + "/" + relativePath.TrimStart('/');
        }

        private static string CombineWithStreamingAssetsPath(string configuredPath, string fallbackRelativePath)
        {
            var relativePath = string.IsNullOrWhiteSpace(configuredPath) ? fallbackRelativePath : configuredPath.Trim();

            if (relativePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return relativePath;
            }

            if (relativePath.StartsWith("StreamingAssets/", StringComparison.OrdinalIgnoreCase))
            {
                relativePath = relativePath.Substring("StreamingAssets/".Length);
            }

            var basePath = (Application.streamingAssetsPath ?? "StreamingAssets").TrimEnd('/');
            return basePath + "/" + relativePath.TrimStart('/');
        }

        private void RegisterUiEvents()
        {
            if (sendButton != null)
            {
                sendButton.onClick.AddListener(OnSendClicked);
            }

            if (surrenderButton != null)
            {
                surrenderButton.onClick.AddListener(OnSurrenderClicked);
            }

            if (textInput != null)
            {
                textInput.onSubmit.AddListener(OnTextSubmit);
            }
        }

        private void UnregisterUiEvents()
        {
            if (sendButton != null)
            {
                sendButton.onClick.RemoveListener(OnSendClicked);
            }

            if (surrenderButton != null)
            {
                surrenderButton.onClick.RemoveListener(OnSurrenderClicked);
            }

            if (textInput != null)
            {
                textInput.onSubmit.RemoveListener(OnTextSubmit);
            }
        }

        private void StartNewGame()
        {
            if (!isModelLoaded)
            {
                AddLog("モデルがまだ読み込まれていません。");
                return;
            }

            if (!TryPickSecretWord(out var secretWord))
            {
                AddLog("辞書に単語がありません。ゲームを開始できません。");
                return;
            }

            currentSecretWord = secretWord;
            currentSecretWordNormalized = NormalizeForContains(secretWord);
            isGameActive = true;
            
            if (client != null)
            {
                client.ClearAllMessages();
            }

            if (textInput != null)
            {
                textInput.text = string.Empty;
            }

            logBuilder.Clear();
            
            UpdateWordLabel($"隠し単語: {currentSecretWord}");
            AddLog($"ゲーム開始 / 隠し単語: {currentSecretWord}");
            AddLog("NPCとの会話中に、隠し単語を含むセリフを返させたら勝利です。");
            AddLog("NPC: こんにちは。今日はどんな話をしようか？");

            SetControlsInteractable(true);
            ChangeStartButtonToSurrenderButton();
        }

        private void OnTextSubmit(string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                OnSendClicked();
            }
        }

        private void OnSendClicked()
        {
            if (!isGameActive || isBusy)
            {
                return;
            }

            var playerLine = (textInput != null ? textInput.text : string.Empty).Trim();

            if (string.IsNullOrEmpty(playerLine))
            {
                AddLog("発言を入力してください。");
                return;
            }

            if (textInput != null)
            {
                textInput.text = string.Empty;
            }

            logBuilder.Clear();
            AddLog($"Player: {playerLine}");
            StartCoroutine(RequestNpcReply(playerLine));
        }

        private void OnSurrenderClicked()
        {
            if (!isGameActive || isBusy)
            {
                return;
            }

            EndGame(false, "プレイヤーが降参しました。");
        }

        private IEnumerator RequestNpcReply(string playerLine)
        {
            SetBusy(true);

            var promptBuilder = new StringBuilder();
            promptBuilder.AppendLine("プレイヤーの発言に対して、ゲーム内NPCとして自然な日本語で返答してください。");
            promptBuilder.AppendLine("返答は1〜3文で、セリフ本文のみを返してください。");
            promptBuilder.AppendLine($"プレイヤー発言: {playerLine}");

            var options = new ChatRequestOptions
            {
                WaitIfBusy = true,
                Priority = 100,
                Temperature = 0.7f,
                SystemPrompt = BuildNpcSystemPrompt()
            };

            var finished = false;

            yield return client.SendMessageAsync(
                promptBuilder.ToString(),
                response =>
                {
                    var npcReply = NormalizeModelOutput(response.Content);
                    if (string.IsNullOrWhiteSpace(npcReply))
                    {
                        npcReply = "うまく言葉が出てこないな。もう一度話しかけてくれる？";
                    }

                    AddLog($"NPC: {npcReply}");

                    if (DoesNpcLineContainSecretWord(npcReply))
                    {
                        EndGame(true, $"NPCが隠し単語『{currentSecretWord}』を口にしました。");
                    }
                    else
                    {
                        SetBusy(false);
                    }

                    finished = true;
                },
                error =>
                {
                    AddLog($"NPC応答エラー: {error.Message}");
                    SetBusy(false);
                    finished = true;
                },
                options
            );

            while (!finished)
            {
                yield return null;
            }
        }

        private static string NormalizeReading(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            var text = raw.Trim().Replace(" ", string.Empty).Replace("　", string.Empty);
            var sb = new StringBuilder(text.Length);

            foreach (var c in text)
            {
                if (c >= 'ァ' && c <= 'ヶ')
                {
                    sb.Append((char)(c - 0x60));
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        private bool LoadDictionaryFromCsv()
        {
            dictionaryWords.Clear();

            var csvAsset = Resources.Load<TextAsset>(dictionaryCsvResourcePath);
            if (csvAsset == null)
            {
                Debug.LogError($"[WordTrap] 辞書CSVが見つかりません。Resources/{dictionaryCsvResourcePath}.csv を配置してください。");
                return false;
            }

            var lines = csvAsset.text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            var wordsSet = new HashSet<string>(StringComparer.Ordinal);

            foreach (var rawLine in lines)
            {
                var line = (rawLine ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var columns = ParseCsvLine(line);
                if (columns.Count < 1)
                {
                    continue;
                }

                var word = columns[0].Trim();
                if (word == "単語" || word.Equals("word", StringComparison.OrdinalIgnoreCase) || word.Length < 2)
                {
                    continue;
                }

                if (wordsSet.Add(word))
                {
                    dictionaryWords.Add(word);
                }
            }

            if (dictionaryWords.Count == 0)
            {
                dictionaryWords.AddRange(new[] { "りんご", "ねこ", "さくら", "とけい", "でんしゃ", "うみ", "ほし" });
            }

            return dictionaryWords.Count > 0;
        }

        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            var cell = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }

                    continue;
                }

                if (c == ',' && !inQuotes)
                {
                    result.Add(cell.ToString());
                    cell.Length = 0;
                    continue;
                }

                cell.Append(c);
            }

            result.Add(cell.ToString());
            return result;
        }

        private bool TryPickSecretWord(out string secretWord)
        {
            secretWord = string.Empty;
            if (dictionaryWords.Count == 0)
            {
                return false;
            }

            secretWord = dictionaryWords[random.Next(dictionaryWords.Count)];
            return !string.IsNullOrWhiteSpace(secretWord);
        }

        private bool DoesNpcLineContainSecretWord(string npcLine)
        {
            if (string.IsNullOrWhiteSpace(npcLine) || string.IsNullOrWhiteSpace(currentSecretWordNormalized))
            {
                return false;
            }

            var normalizedNpcLine = NormalizeForContains(npcLine);
            return normalizedNpcLine.Contains(currentSecretWordNormalized, StringComparison.Ordinal);
        }

        private static string NormalizeForContains(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = NormalizeReading(value);
            return Regex.Replace(normalized, "[\\s　]", string.Empty).ToLowerInvariant();
        }

        private void SetBusy(bool busy)
        {
            isBusy = busy;
            SetControlsInteractable(!busy);
        }

        private void SetControlsInteractable(bool interactable)
        {
            var canInteract = interactable && isGameActive;
            
            if (sendButton != null)
            {
                sendButton.interactable = canInteract;
            }

            if (surrenderButton != null)
            {
                surrenderButton.interactable = canInteract;
            }

            if (textInput != null)
            {
                textInput.interactable = canInteract;
            }
        }

        private void UpdateWordLabel(string message)
        {
            if (wordLabel != null)
            {
                wordLabel.text = message;
            }
        }

        private void AddLog(string message)
        {
            logBuilder.AppendLine(message);
            
            if (logLabel != null)
            {
                logLabel.text = logBuilder.ToString();
            }
        }

        private void EndGame(bool playerWon, string reason)
        {
            isGameActive = false;
            isBusy = false;
            
            var result = playerWon ? "プレイヤー勝利" : "NPCの勝利";
            AddLog($"{result}: {reason}");
            
            SetControlsInteractable(false);
            ChangeSurrenderButtonToStartButton();
        }

        private void ChangeSurrenderButtonToStartButton()
        {
            if (surrenderButton != null)
            {
                var buttonText = surrenderButton.GetComponentInChildren<TMP_Text>();
                if (buttonText != null)
                {
                    buttonText.text = "スタート";
                }
                surrenderButton.onClick.RemoveListener(OnSurrenderClicked);
                surrenderButton.onClick.AddListener(StartNewGame);
                surrenderButton.interactable = true;
            }
        }

        private void ChangeStartButtonToSurrenderButton()
        {
            if (surrenderButton != null)
            {
                var buttonText = surrenderButton.GetComponentInChildren<TMP_Text>();
                if (buttonText != null)
                {
                    buttonText.text = "降参";
                }
                surrenderButton.onClick.RemoveListener(StartNewGame);
                surrenderButton.onClick.AddListener(OnSurrenderClicked);
            }
        }

        private static string NormalizeModelOutput(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            var text = raw.Trim();
            text = Regex.Replace(text, "```(?:json)?", string.Empty, RegexOptions.IgnoreCase);
            text = text.Replace("```", string.Empty).Trim();
            return text;
        }

        private string BuildNpcSystemPrompt()
        {
            var sb = new StringBuilder();
            sb.AppendLine("あなたはゲーム内の気さくなNPCです。");
            sb.AppendLine("プレイヤーから話しかけられたら、自然な会話として返答してください。");
            sb.AppendLine("返答はセリフ本文のみ、1〜3文で簡潔に答えてください。");
            sb.AppendLine("ゲームの内部ルール、判定、隠し単語の存在には触れないでください。");
            return sb.ToString();
        }
    }
}
