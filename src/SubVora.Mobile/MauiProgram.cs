using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using Refit;
using SubVora.Mobile.Api;
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
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		builder.Services.AddSingleton<ITokenStore, SecureStorageTokenStore>();

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
		builder.Services.AddHttpClient("AuthRefresh", client => client.BaseAddress = new Uri(ApiConfig.BaseAddress));

		builder.Services.AddSingleton(sp => new AuthDelegatingHandler(
			sp.GetRequiredService<ITokenStore>(),
			sp.GetRequiredService<IHttpClientFactory>().CreateClient("AuthRefresh")));

		// IAuthApi must not chain AuthDelegatingHandler - login/register/refresh calls
		// themselves would otherwise loop back through the 401-refresh logic.
		builder.Services.AddRefitClient<IAuthApi>(refitSettings)
			.ConfigureHttpClient(client => client.BaseAddress = new Uri(ApiConfig.BaseAddress));

		builder.Services.AddRefitClient<IUsersApi>(refitSettings)
			.ConfigureHttpClient(client => client.BaseAddress = new Uri(ApiConfig.BaseAddress))
			.AddHttpMessageHandler(sp => sp.GetRequiredService<AuthDelegatingHandler>());

		builder.Services.AddRefitClient<ISubscriptionsApi>(refitSettings)
			.ConfigureHttpClient(client => client.BaseAddress = new Uri(ApiConfig.BaseAddress))
			.AddHttpMessageHandler(sp => sp.GetRequiredService<AuthDelegatingHandler>());

		builder.Services.AddRefitClient<ICategoriesApi>(refitSettings)
			.ConfigureHttpClient(client => client.BaseAddress = new Uri(ApiConfig.BaseAddress))
			.AddHttpMessageHandler(sp => sp.GetRequiredService<AuthDelegatingHandler>());

		builder.Services.AddRefitClient<IPaymentSourcesApi>(refitSettings)
			.ConfigureHttpClient(client => client.BaseAddress = new Uri(ApiConfig.BaseAddress))
			.AddHttpMessageHandler(sp => sp.GetRequiredService<AuthDelegatingHandler>());

		builder.Services.AddRefitClient<IDashboardApi>(refitSettings)
			.ConfigureHttpClient(client => client.BaseAddress = new Uri(ApiConfig.BaseAddress))
			.AddHttpMessageHandler(sp => sp.GetRequiredService<AuthDelegatingHandler>());

		builder.Services.AddTransient<AppShell>();
		builder.Services.AddTransient<LoginViewModel>();
		builder.Services.AddTransient<LoginPage>();
		builder.Services.AddTransient<RegisterViewModel>();
		builder.Services.AddTransient<RegisterPage>();
		builder.Services.AddTransient<DashboardViewModel>();
		builder.Services.AddTransient<DashboardPage>();
		builder.Services.AddTransient<SubscriptionListViewModel>();
		builder.Services.AddTransient<SubscriptionListPage>();
		builder.Services.AddTransient<CategoriesViewModel>();
		builder.Services.AddTransient<CategoriesPage>();
		builder.Services.AddTransient<SubscriptionDetailViewModel>();
		builder.Services.AddTransient<SubscriptionDetailPage>();

		return builder.Build();
	}
}
