using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Maui;
using CommunityToolkit.Mvvm.Messaging;
using Plugin.LocalNotification;
using Microsoft.Extensions.Logging;
using Refit;
using SubVora.Mobile.Api;
using SubVora.Mobile.Notifications;
using SubVora.Mobile.Services;
using SubVora.Mobile.ViewModels;
using SubVora.Mobile.Views;

namespace SubVora.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.UseLocalNotification()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if ANDROID
		// Android tints every tab icon from Shell's TabBar colours, which flattens the five
		// coloured tab SVGs into one hue. See ColorfulTabsShellRenderer.
		builder.ConfigureMauiHandlers(handlers =>
			handlers.AddHandler<Shell, Platforms.Android.ColorfulTabsShellRenderer>());
#endif

#if DEBUG
		builder.Logging.AddDebug();
#endif

		// One messenger for the whole app: the burn-rate banner listens for subscription changes
		// published by the detail, list and settings view models.
		builder.Services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);

		builder.Services.AddSingleton<ITokenStore, SecureStorageTokenStore>();

		builder.Services.AddSingleton<IUserPrompt, ShellUserPrompt>();

		builder.Services.AddSingleton<IConnectivityService, ConnectivityService>();

		builder.Services.AddSingleton<IThemeService, ThemeService>();

		builder.Services.AddSingleton<IRenewalNotificationScheduler, LocalRenewalNotificationScheduler>();

		builder.Services.AddSingleton<ILocalCacheService>(_ =>
			new SqliteLocalCacheService(Path.Combine(FileSystem.AppDataDirectory, "subvora_cache.db3")));

		var refitSettings = new RefitSettings
		{
			ContentSerializer = new SystemTextJsonContentSerializer(new JsonSerializerOptions
			{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
				Converters = { new JsonStringEnumConverter() },
			}),
		};

		// Plain HttpClient with no AuthDelegatingHandler attached, used only to call the
		// refresh endpoint so a 401 during refresh can never recurse back into the handler.
		builder.Services.AddHttpClient("AuthRefresh", client =>
		{
			client.BaseAddress = new Uri(ApiConfig.BaseAddress);
			client.Timeout = ApiConfig.RefreshTimeout;
		});

		// Singleton: one refresh lock and one SessionExpired event for the whole app.
		builder.Services.AddSingleton(sp => new SessionRefresher(
			sp.GetRequiredService<ITokenStore>(),
			sp.GetRequiredService<IHttpClientFactory>().CreateClient("AuthRefresh"),
			sp.GetRequiredService<ILocalCacheService>()));

		// Transient: HttpClientFactory sets InnerHandler on each instance it is given, so sharing
		// one across the Refit clients below throws as soon as the second client is built.
		builder.Services.AddTransient(sp => new AuthDelegatingHandler(
			sp.GetRequiredService<ITokenStore>(),
			sp.GetRequiredService<SessionRefresher>()));

		// IAuthApi must not chain AuthDelegatingHandler - login/register/refresh calls
		// themselves would otherwise loop back through the 401-refresh logic.
		builder.Services.AddRefitClient<IAuthApi>(refitSettings)
			.ConfigureHttpClient(ConfigureApiClient);

		builder.Services.AddRefitClient<IUsersApi>(refitSettings)
			.ConfigureHttpClient(ConfigureApiClient)
			.AddHttpMessageHandler(sp => sp.GetRequiredService<AuthDelegatingHandler>());

		builder.Services.AddRefitClient<ISubscriptionsApi>(refitSettings)
			.ConfigureHttpClient(ConfigureApiClient)
			.AddHttpMessageHandler(sp => sp.GetRequiredService<AuthDelegatingHandler>());

		builder.Services.AddRefitClient<ICategoriesApi>(refitSettings)
			.ConfigureHttpClient(ConfigureApiClient)
			.AddHttpMessageHandler(sp => sp.GetRequiredService<AuthDelegatingHandler>());

		builder.Services.AddRefitClient<IPaymentSourcesApi>(refitSettings)
			.ConfigureHttpClient(ConfigureApiClient)
			.AddHttpMessageHandler(sp => sp.GetRequiredService<AuthDelegatingHandler>());

		builder.Services.AddRefitClient<IDashboardApi>(refitSettings)
			.ConfigureHttpClient(ConfigureApiClient)
			.AddHttpMessageHandler(sp => sp.GetRequiredService<AuthDelegatingHandler>());

		builder.Services.AddTransient<AppShell>();
		builder.Services.AddTransient<LoginViewModel>();
		builder.Services.AddTransient<LoginPage>();
		builder.Services.AddTransient<ForgotPasswordViewModel>();
		builder.Services.AddTransient<ForgotPasswordPage>();
		builder.Services.AddTransient<RegisterViewModel>();
		builder.Services.AddTransient<RegisterPage>();
		// Singleton, unlike every other view model: AppShell's banner and DashboardPage bind to
		// the same instance, so one fetch feeds both and they cannot drift apart.
		builder.Services.AddSingleton<DashboardViewModel>();
		builder.Services.AddTransient<DashboardPage>();
		builder.Services.AddTransient<SubscriptionListViewModel>();
		builder.Services.AddTransient<SubscriptionListPage>();
		builder.Services.AddTransient<CategoriesViewModel>();
		builder.Services.AddTransient<CategoriesPage>();
		builder.Services.AddTransient<IDebouncer, Debouncer>();
		builder.Services.AddTransient<SubscriptionDetailViewModel>();
		builder.Services.AddTransient<SubscriptionDetailPage>();
		builder.Services.AddTransient<PaymentSourcesViewModel>();
		builder.Services.AddTransient<PaymentSourcesPage>();
		builder.Services.AddTransient<SettingsViewModel>();
		builder.Services.AddTransient<SettingsPage>();

		return builder.Build();
	}

	/// <summary>
	/// What every Refit client's HttpClient looks like. One place, so a client added later cannot
	/// quietly ship without the timeout - which is how all six ended up on HttpClient's 100-second
	/// default to begin with.
	/// </summary>
	private static void ConfigureApiClient(HttpClient client)
	{
		client.BaseAddress = new Uri(ApiConfig.BaseAddress);
		client.Timeout = ApiConfig.RequestTimeout;
	}
}
