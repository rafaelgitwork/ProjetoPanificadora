using Microsoft.Extensions.Logging;

namespace Panificadora.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		builder.Services.AddSingleton<Panificadora.App.Dados.BaseLocal>();
        builder.Services.AddSingleton<Panificadora.App.Servicos.Trabalho>();
        return builder.Build();
	}
}
