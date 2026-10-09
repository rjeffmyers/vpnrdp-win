using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VpnRdp.Models;
using VpnRdp.Services;

namespace VpnRdp.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ProfileStore _store = new();
    private readonly IVpnPrompter _prompter;

    public MainViewModel(IVpnPrompter prompter)
    {
        _prompter = prompter;
        foreach (var p in _store.Load())
            Profiles.Add(new ProfileItemViewModel(p, prompter));
        Selected = Profiles.FirstOrDefault();
        Profiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoProfiles));
        RefreshOpenVpnStatus();
    }

    public bool HasNoProfiles => Profiles.Count == 0;

    public ObservableCollection<ProfileItemViewModel> Profiles { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ProfileItemViewModel? _selected;

    public bool HasSelection => Selected is not null;

    [ObservableProperty] private string _openVpnStatus = "";
    [ObservableProperty] private bool _setupNeeded;

    public void RefreshOpenVpnStatus()
    {
        var install = OpenVpnLocator.Find();
        if (install is null)
        {
            OpenVpnStatus = "OpenVPN Community is not installed. Click Setup to install it (needs an administrator once).";
            SetupNeeded = true;
        }
        else if (!install.InteractiveServiceRunning)
        {
            OpenVpnStatus = $"OpenVPN {install.Version} is installed but the OpenVPN Interactive Service is not running. Click Setup to fix.";
            SetupNeeded = true;
        }
        else if (!OpenVpnLocator.IsCurrentUserAuthorized(install))
        {
            OpenVpnStatus = $"Your account is not in the \"{install.AdminGroup}\" group yet. Click Setup to add it (needs an administrator once).";
            SetupNeeded = true;
        }
        else
        {
            OpenVpnStatus = $"OpenVPN {install.Version} ready";
            SetupNeeded = false;
        }
    }

    public ProfileItemViewModel Add(Profile p)
    {
        var vm = new ProfileItemViewModel(p, _prompter);
        Profiles.Add(vm);
        Save();
        Selected = vm;
        return vm;
    }

    public void Replace(ProfileItemViewModel vm, Profile updated)
    {
        vm.Update(updated);
        Save();
    }

    public void Remove(ProfileItemViewModel vm)
    {
        Profiles.Remove(vm);
        try
        {
            CredentialStore.Delete(CredentialStore.VpnTarget(vm.Profile.Id));
            var dir = Path.Combine(AppPaths.OvpnDir, vm.Profile.Id);
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex)
        {
            Log.Warn($"Cleanup after deleting {vm.Name} failed: {ex.Message}");
        }
        Save();
        Selected = Profiles.FirstOrDefault();
    }

    public void Save() => _store.Save(Profiles.Select(p => p.Profile));

    public bool AnyActive => Profiles.Any(p => p.IsActive);
}
