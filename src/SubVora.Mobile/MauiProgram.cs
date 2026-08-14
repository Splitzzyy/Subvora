using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Maui;
using CommunityToolkit.Mvvm.Messaging;
using Plugin.LocalNotification;
using Microsoft.Extensions.DependencyInjection;
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

		AddSubVoraServices(builder.Services);

		return builder.Build();
	}

	/// <summary>
	/// Every service registration the app makes, split from <see cref="CreateMauiApp"/> so it can be
	/// composed into a plain <see cref="IServiceCollection"/> without a MAUI host.
	/// <para>
	/// That is what lets <c>RefitClientCompositionTests</c> assert how the Refit clients are wired -
	/// specifically that each one whose endpoints require authentication carries
	/// <see cref="AuthDelegatingHandler"/>. Nothing else asserted that, which is how change-password
	/// and logout shipped calling <c>[Authorize]</c> endpoints with no token at all.
	/// </para>
	/// <para>
	/// A pure move: the order and content of the registrations below are exactly what
	/// <see cref="CreateMauiApp"/> used to perform inline.
	/// </para>
	/// </summary>
	public static void AddSubVoraServices(IServiceCollection services)
	{
		// One messenger for the whole app: the burn-rate banner listens for subscription changes
		// published by the detail, list and settings view models.
		services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);

		services.AddSingleton<ITokenStore, SecureStorageTokenStore>();

		services.AddSingleton<IUserPrompt, ShellUserPrompt>();

		services.AddSingleton<IConnectivityService, ConnectivityService>();

		services.AddSingleton<IThemeService, ThemeService>();

		services.AddSingleton<IRenewalNotificationScheduler, LocalRenewalNotificationScheduler>();

		services.AddSingleton<ILocalCacheService>(_ =>
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
		services.AddHttpClient("AuthRefresh", client =>
		{
			client.BaseAddress = new Uri(ApiConfig.BaseAddress);
			client.Timeout = ApiConfig.RefreshTimeout;
		});

		// Singleton: one refresh lock and one SessionExpired event for the whole app.
		services.AddSingleton(sp => new SessionRefresher(
			sp.GetRequiredService<ITokenStore>(),
			sp.GetRequiredService<IHttpClientFactory>().CreateClient("AuthRefresh"),
			sp.GetRequiredService<ILocalCacheService>()));

		// Transient: HttpClientFactory sets InnerHandler on each instance it is given, so sharing
		// one across the Refit clients below throws as soon as the second client is built.
		services.AddTransient(sp => new AuthDelegatingHandler(
			sp.GetRequiredService<ITokenStore>(),
			sp.GetRequiredService<SessionRefresher>()));

		// IAuthApi carries only the endpoints that take no bearer token, and must not chain
		// AuthDelegatingHandler - login/register/refresh calls themselves would otherwise loop back
		// through the 401-refresh logic.
		services.AddRefitClient<IAuthApi>(refitSettings)
			.ConfigureHttpClient(ConfigureApiClient);

		// The auth endpoints that *do* require a token. Safe to chain the handler because neither
		// call is /auth/refresh, so the handler's 401 path cannot recurse into refresh. Without this
		// registration the calls went out unauthenticated against [Authorize] endpoints - see
		// IAccountApi.
		services.AddRefitClient<IAccountApi>(refitSettings)
			.ConfigureHttpClient(ConfigureApiClient)
			.AddHttpMessageHandler(sp => sp.GetRequiredService<AuthDelegatingHandler>());

		services.AddRefitClient<IUsersApi>(refitSettings)
			.ConfigureHttpClient(ConfigureApiClient)
			.AddHttpMessageHandler(sp => sp.GetRequiredService<AuthDelegatingHandler>());

		services.AddRefitClient<ISubscriptionsApi>(refitSettings)
			.ConfigureHttpClient(ConfigureApiClient)
			.AddHttpMessageHandler(sp => sp.GetRequiredService<AuthDelegatingHandler>());

		services.AddRefitClient<ICategoriesApi>(refitSettings)
			.ConfigureHttpClient(ConfigureApiClient)
			.AddHttpMessageHandler(sp => sp.GetRequiredService<AuthDelegatingHandler>());

		services.AddRefitClient<IPaymentSourcesApi>(refitSettings)
			.ConfigureHttpClient(ConfigureApiClient)
			.AddHttpMessageHandler(sp => sp.GetRequiredService<AuthDelegatingHandler>());

		services.AddRefitClient<IDashboardApi>(refitSettings)
			.ConfigureHttpClient(ConfigureApiClient)
			.AddHttpMessageHandler(sp => sp.GetRequiredService<AuthDelegatingHandler>());

		services.AddTransient<AppShell>();
		services.AddTransient<LoginViewModel>();
		services.AddTransient<LoginPage>();
		services.AddTransient<ForgotPasswordViewModel>();
		services.AddTransient<ForgotPasswordPage>();
		services.AddTransient<RegisterViewModel>();
		services.AddTransient<RegisterPage>();
		// Singleton, unlike every other view model: AppShell's banner and DashboardPage bind to
		// the same instance, so one fetch feeds both and they cannot drift apart.
		services.AddSingleton<DashboardViewModel>();
		services.AddTransient<DashboardPage>();
		services.AddTransient<SubscriptionListViewModel>();
		services.AddTransient<SubscriptionListPage>();
		services.AddTransient<CategoriesViewModel>();
		services.AddTransient<CategoriesPage>();
		services.AddTransient<IDebouncer, Debouncer>();
		services.AddTransient<SubscriptionDetailViewModel>();
		services.AddTransient<SubscriptionDetailPage>();
		services.AddTransient<PaymentSourcesViewModel>();
		services.AddTransient<PaymentSourcesPage>();
		services.AddTransient<SettingsViewModel>();
		services.AddTransient<SettingsPage>();
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
