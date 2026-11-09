using Panificadora.App.Servicos;

namespace Panificadora.App;

public partial class App : Application
{
    private readonly Trabalho trabalho;

    public App(Trabalho trabalho)
    {
        InitializeComponent();
        this.trabalho = trabalho;
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new CampoPage(trabalho));
}
