using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Windows;

namespace MiGestorBDAccesible.Servicios
{
    public enum RespuestaGuardar
    {
        Guardar,
        Descartar,
        Cancelar
    }

    public class ArchivosService
    {
        public const string FiltroSql = "Archivos SQL (*.sql)|*.sql|Todos los archivos (*.*)|*.*";
        public const string FiltroCsv = "Archivos CSV (*.csv)|*.csv|Todos los archivos (*.*)|*.*";
        public const string FiltroHtml = "Páginas HTML (*.html)|*.html|Todos los archivos (*.*)|*.*";

        private static readonly Encoding _utf8ConMarca = new UTF8Encoding(true);

        private string _ultimaCarpeta = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        public string? ElegirParaAbrir(string titulo, string filtro)
        {
            var dialogo = new OpenFileDialog
            {
                Title = titulo,
                Filter = filtro,
                InitialDirectory = _ultimaCarpeta,
                CheckFileExists = true
            };

            return dialogo.ShowDialog(Application.Current.MainWindow) == true ? Recordar(dialogo.FileName) : null;
        }

        public string? ElegirParaGuardar(string titulo, string filtro, string nombreSugerido, string extension)
        {
            var dialogo = new SaveFileDialog
            {
                Title = titulo,
                Filter = filtro,
                FileName = nombreSugerido,
                DefaultExt = extension,
                AddExtension = true,
                OverwritePrompt = true,
                InitialDirectory = _ultimaCarpeta
            };

            return dialogo.ShowDialog(Application.Current.MainWindow) == true ? Recordar(dialogo.FileName) : null;
        }

        public RespuestaGuardar PreguntarGuardarCambios(string nombre)
        {
            var respuesta = MessageBox.Show(
                Application.Current.MainWindow,
                $"La consulta {nombre} tiene cambios sin guardar. ¿Quieres guardarlos?",
                "Mi Gestor BD Accesible",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            return respuesta switch
            {
                MessageBoxResult.Yes => RespuestaGuardar.Guardar,
                MessageBoxResult.No => RespuestaGuardar.Descartar,
                _ => RespuestaGuardar.Cancelar
            };
        }

        public async Task<string> LeerAsync(string ruta)
        {
            var texto = await File.ReadAllTextAsync(ruta);
            return texto.Replace("\r\n", "\n");
        }

        public Task GuardarAsync(string ruta, string texto) =>
            File.WriteAllTextAsync(ruta, texto.Replace("\r\n", "\n").Replace("\n", "\r\n"), _utf8ConMarca);

        public void Copiar(string texto) => Clipboard.SetText(texto);

        private string Recordar(string ruta)
        {
            _ultimaCarpeta = Path.GetDirectoryName(ruta) ?? _ultimaCarpeta;
            return ruta;
        }
    }
}
