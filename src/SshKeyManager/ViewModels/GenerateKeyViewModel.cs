using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SshKeyManager.Models;
using SshKeyManager.Services;

namespace SshKeyManager.ViewModels;

public sealed partial class GenerateKeyViewModel : ObservableObject
{
    private readonly SshKeyService _keys;

    public GenerateKeyViewModel(SshKeyService keys)
    {
        _keys = keys;

        Algorithms = new[]
        {
            new AlgorithmOption("Ed25519 (recommended)", KeyAlgorithm.Ed25519),
            new AlgorithmOption("RSA", KeyAlgorithm.Rsa),
            new AlgorithmOption("ECDSA", KeyAlgorithm.Ecdsa)
        };

        SelectedAlgorithm = Algorithms[0];
        Comment = $"{Environment.UserName}@{Environment.MachineName}";
        Name = keys.SuggestKeyName("Ed25519");
        RebuildBits();
    }

    public event EventHandler<bool>? CloseRequested;

    public IReadOnlyList<AlgorithmOption> Algorithms { get; }
    public ObservableCollection<int> BitsOptions { get; } = new();
    public string FolderPath => _keys.SshDirectory;
    public SshKeyInfo? CreatedKey { get; private set; }

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string comment = string.Empty;

    [ObservableProperty]
    private string passphrase = string.Empty;

    [ObservableProperty]
    private string confirmPassphrase = string.Empty;

    [ObservableProperty]
    private bool showPassphrase;

    [ObservableProperty]
    private bool addToAgent = true;

    [ObservableProperty]
    private bool overwrite;

    [ObservableProperty]
    private AlgorithmOption? selectedAlgorithm;

    [ObservableProperty]
    private int selectedBits;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool nameTaken;

    public bool IsBitsEnabled => SelectedAlgorithm?.Kind is KeyAlgorithm.Rsa or KeyAlgorithm.Ecdsa;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    partial void OnSelectedAlgorithmChanged(AlgorithmOption? value)
    {
        RebuildBits();
        OnPropertyChanged(nameof(IsBitsEnabled));
    }

    partial void OnNameChanged(string value) => CheckName();

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    private void RebuildBits()
    {
        var kind = SelectedAlgorithm?.Kind ?? KeyAlgorithm.Ed25519;
        var options = kind switch
        {
            KeyAlgorithm.Rsa => new[] { 2048, 3072, 4096 },
            KeyAlgorithm.Ecdsa => new[] { 256, 384, 521 },
            _ => new[] { 256 }
        };

        BitsOptions.Clear();
        foreach (var option in options)
            BitsOptions.Add(option);

        SelectedBits = options[^1];
    }

    private void CheckName()
    {
        var candidate = Name.Trim();
        if (candidate.Length == 0)
        {
            NameTaken = false;
            return;
        }

        try
        {
            var directory = _keys.SshDirectory;
            NameTaken = File.Exists(Path.Combine(directory, candidate)) ||
                        File.Exists(Path.Combine(directory, candidate + ".pub"));
        }
        catch
        {
            NameTaken = false;
        }

        if (!NameTaken)
            Overwrite = false;
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (IsBusy)
            return;

        ErrorMessage = null;

        var name = Name.Trim();
        if (name.Length == 0)
        {
            ErrorMessage = "Enter a file name for the key.";
            return;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
        {
            ErrorMessage = "The file name contains characters that are not allowed.";
            return;
        }

        if (Passphrase != ConfirmPassphrase)
        {
            ErrorMessage = "The passphrases do not match.";
            return;
        }

        if (NameTaken && !Overwrite)
        {
            ErrorMessage = "A key with this name already exists. Tick \"Overwrite existing key\" to replace it.";
            return;
        }

        var algorithm = SelectedAlgorithm?.Kind ?? KeyAlgorithm.Ed25519;
        var bits = algorithm == KeyAlgorithm.Ed25519 ? 256 : SelectedBits;

        var request = new KeyGenerationRequest(
            name,
            algorithm,
            bits,
            Comment.Trim(),
            string.IsNullOrEmpty(Passphrase) ? null : Passphrase,
            AddToAgent);

        IsBusy = true;
        try
        {
            CreatedKey = await _keys.GenerateAsync(request, Overwrite);
            CloseRequested?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);
}
