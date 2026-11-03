using MiGestorBDAccesible.Core;
using MiGestorBDAccesible.Core.Actualizaciones;
using MiGestorBDAccesible.Core.Ayuda;
using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Descripcion;
using MiGestorBDAccesible.Core.Proveedores;
using MiGestorBDAccesible.Core.Trabajo;
using MiGestorBDAccesible.Servicios;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Windows;

namespace MiGestorBDAccesible
{
    public partial class MainWindow : Window
    {
        private readonly ProveedoresService _proveedores;
        private bool _cerrando;

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
            if (_cerrando || !_proveedores.Todos.Any(p => p.TieneCambiosPendientes()) && !Core.Edicion.RegistroSesionesEdicion.HayCambiosPendientes)
            {
                return;
            }

            e.Cancel = true;

            var respuesta = MessageBox.Show(this,
                "Hay cambios sin confirmar. ¿Quieres guardarlos en la base de datos antes de salir?\n\nSí: confirmar los cambios (COMMIT).\nNo: deshacer los cambios (ROLLBACK).\nCancelar: seguir en la aplicación.",
                "Cambios sin confirmar",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Cancel);

            if (respuesta == MessageBoxResult.Cancel)
            {
                if (ActualizacionPendiente.Instalador is not null)
                {
                    ActualizacionPendiente.AvisarCierreCancelado();
                }

                return;
            }

            foreach (var proveedor in _proveedores.Todos)
            {
                await proveedor.FinalizarSesionAsync(null, respuesta == MessageBoxResult.Yes);
            }

            await Core.Edicion.RegistroSesionesEdicion.TerminarTodasAsync(respuesta == MessageBoxResult.Yes);

            _cerrando = true;
            Close();
        }

        private void ConfigurarVistaWeb(object? sender, BlazorWebViewInitializedEventArgs e)
        {
            var ajustes = e.WebView.CoreWebView2.Settings;
            ajustes.AreBrowserAcceleratorKeysEnabled = false;
            ajustes.IsStatusBarEnabled = false;
        }
    }
}
