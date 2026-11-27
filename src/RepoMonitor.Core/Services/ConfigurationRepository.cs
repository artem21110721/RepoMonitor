using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RepoMonitor.Core.Abstractions;
using RepoMonitor.Core.Models;

namespace RepoMonitor.Core.Services;

public class ConfigurationRepository
{
    private readonly ISecretProtector _secretProtector;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public string FilePath { get; }

    public ConfigurationRepository(ISecretProtector secretProtector, string? filePath = null)
    {
        _secretProtector = secretProtector;
        FilePath = string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RepoMonitor",
                "config.json")
            : filePath;
    }

    public AppConfig Load()
    {
        if (!File.Exists(FilePath))
        {
            return new AppConfig();
        }

        try
        {
            string json = File.ReadAllText(FilePath, Encoding.UTF8);
            JsonNode? root = JsonNode.Parse(json);
            if (root is not JsonObject rootObj)
            {
                return new AppConfig();
            }

            TransformSecrets(rootObj, encrypt: false);
            return rootObj.Deserialize<AppConfig>(_jsonOptions) ?? new AppConfig();
        }
        catch (JsonException)
        {
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        string? directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        JsonNode? root = JsonSerializer.SerializeToNode(config, _jsonOptions);
        if (root is JsonObject rootObj)
        {
            TransformSecrets(rootObj, encrypt: true);
        }

        string json = root?.ToJsonString(_jsonOptions) ?? "{}";
        string tempPath = $"{FilePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            File.WriteAllText(tempPath, json, Encoding.UTF8);
            File.Move(tempPath, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private void TransformSecrets(JsonObject rootObj, bool encrypt)
    {
        if (!rootObj.TryGetPropertyValue("Sources", out JsonNode? sourcesNode) ||
            sourcesNode is not JsonArray sourcesArray)
        {
            return;
        }

        foreach (JsonNode? item in sourcesArray)
        {
            if (item is not JsonObject sourceObj)
            {
                continue;
            }

            TransformField(sourceObj, "AccessToken", encrypt);
            TransformField(sourceObj, "Password", encrypt);
        }
    }

    private void TransformField(JsonObject obj, string fieldName, bool encrypt)
    {
        if (!obj.TryGetPropertyValue(fieldName, out JsonNode? node) ||
            node is not JsonValue value ||
            !value.TryGetValue(out string? text) ||
            string.IsNullOrEmpty(text))
        {
            return;
        }

        if (encrypt)
        {
            obj[fieldName] = _secretProtector.Protect(text);
        }
        else
        {
            try
            {
                obj[fieldName] = _secretProtector.Unprotect(text);
            }
            catch
            {
                obj[fieldName] = string.Empty;
            }
        }
    }
}
