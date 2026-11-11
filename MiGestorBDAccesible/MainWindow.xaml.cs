using MiGestorBDAccesible.Core;
using MiGestorBDAccesible.Core.Actualizaciones;
using MiGestorBDAccesible.Core.Autocompletado;
using MiGestorBDAccesible.Core.Ayuda;
using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Edicion;
using MiGestorBDAccesible.Core.Descripcion;
using MiGestorBDAccesible.Core.Proveedores;
using MiGestorBDAccesible.Core.Trabajo;
using MiGestorBDAccesible.Servicios;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace MiGestorBDAccesible
{
    public partial class MainWindow : Window
    {
        private readonly ProveedoresService _proveedores;
        private bool _cerrando;
        private bool _terminandoCierre;

        public MainWindow()
        {
            var servicios = new ServiceCollection();
            servicios.AddWpfBlazorWebView();
#if DEBUG
            servicios.AddBlazorWebViewDeveloperTools();
#endif
            servicios.AddSingleton<AlmacenConexionesService>();
            servicios.AddSingleton<ProveedoresService>();
            servicios.AddSingleton<NarradorTablaService>();
            servicios.AddSingleton<NarradorObjetoService>();
            servicios.AddSingleton<NarradorModeloService>();
            servicios.AddSingleton<ComprobadorModificacionService>();
            servicios.AddSingleton<AyudaService>();
            servicios.AddSingleton<AutocompletadoService>();
            servicios.AddSingleton<ValidadorTablaService>();
            servicios.AddSingleton<AnunciadorService>();
            servicios.AddSingleton<ArchivosService>();
            servicios.AddSingleton<HistorialConsultasService>();
            servicios.AddSingleton<SesionEditorService>();
            servicios.AddSingleton<PreferenciasService>();
            servicios.AddSingleton(new ActualizacionesService(ActualizacionesService.CrearCliente(InformacionAplicacion.VersionCompleta(typeof(MainWindow).Assembly))));

            var proveedor = servicios.BuildServiceProvider();
            _proveedores = proveedor.GetRequiredService<ProveedoresService>();
            Resources.Add("servicios", proveedor);

            InitializeComponent();

            vistaBlazor.BlazorWebViewInitialized += ConfigurarVistaWeb;
            Closing += AlCerrar;
            Closed += AlTerminarDeCerrar;
        }

        private void AlTerminarDeCerrar(object? sender, EventArgs e)
        {
            if (ActualizacionPendiente.Instalador is { } instalador && System.IO.File.Exists(instalador))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(instalador) { UseShellExecute = true });
            }
        }

        private async void AlCerrar(object? sender, CancelEventArgs e)
        {
            if (_cerrando)
            {
                return;
            }

            if (_terminandoCierre)
            {
                e.Cancel = true;
                return;
            }

            if (!CierreAplicacion.HayCambiosPendientes(_proveedores.Todos) && !CierreAplicacion.TieneOperacionesEnCurso(_proveedores.Todos))
            {
                return;
            }

            e.Cancel = true;
            _terminandoCierre = true;
            using var bloqueo = CierreAplicacion.BloquearOperaciones(_proveedores.Todos);
            var focoAnterior = Keyboard.FocusedElement;

            try
            {
                var soloDeshacer = CierreAplicacion.HayCambiosPendientes(_proveedores.Todos)
                    && !CierreAplicacion.TieneOperacionesEnCurso(_proveedores.Todos)
                    && !CierreAplicacion.PuedeConfirmarCambios(_proveedores.Todos);
                var respuesta = MessageBox.Show(this,
                    soloDeshacer
                    ? "Las transacciones están abortadas. Sus cambios no se pueden guardar; solo puedes deshacerlos con ROLLBACK.\n\nAceptar: salir sin guardar.\nCancelar: seguir en la aplicación."
                    : "Hay cambios sin confirmar u operaciones en curso. ¿Quieres guardarlos en la base de datos antes de salir?\n\nSí: confirmar los cambios (COMMIT).\nNo: deshacer los cambios (ROLLBACK).\nCancelar: seguir en la aplicación.",
                    "Cambios sin confirmar",
                    soloDeshacer ? MessageBoxButton.OKCancel : MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning,
                    MessageBoxResult.Cancel);

                while (respuesta != MessageBoxResult.Cancel)
                {
                    vistaBlazor.IsEnabled = false;
                    if (respuesta == MessageBoxResult.No || respuesta == MessageBoxResult.OK)
                    {
                        await CierreAplicacion.SalirSinGuardarAsync(_proveedores.Todos);
                        break;
                    }

                    var resultado = await CierreAplicacion.TerminarAsync(_proveedores.Todos, true);

                    if (resultado.Correcto && !resultado.QuedanCambios)
                    {
                        break;
                    }

                    respuesta = MessageBox.Show(this,
                        CierreAplicacion.TextoFallo(resultado, true),
                        "No se pudo terminar el cierre",
                        CierreAplicacion.OfreceReintentar(resultado, true) ? MessageBoxButton.YesNoCancel : MessageBoxButton.OKCancel,
                        MessageBoxImage.Error,
                        MessageBoxResult.Cancel);
                }

                if (respuesta == MessageBoxResult.Cancel)
                {
                    if (ActualizacionPendiente.Instalador is not null)
                    {
                        ActualizacionPendiente.AvisarCierreCancelado();
                    }

                    return;
                }

                _cerrando = true;
                Close();
            }
            finally
            {
                _terminandoCierre = false;
                vistaBlazor.IsEnabled = true;
                if (!_cerrando)
                {
                    if (focoAnterior is not null) Keyboard.Focus(focoAnterior);
                    else vistaBlazor.Focus();
                }
            }
        }

        private void ConfigurarVistaWeb(object? sender, BlazorWebViewInitializedEventArgs e)
        {
            var ajustes = e.WebView.CoreWebView2.Settings;
            ajustes.AreBrowserAcceleratorKeysEnabled = false;
            ajustes.IsStatusBarEnabled = false;
        }
    }
}
