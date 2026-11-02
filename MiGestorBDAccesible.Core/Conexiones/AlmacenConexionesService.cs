using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiGestorBDAccesible.Core.Conexiones
{
    public class AlmacenConexionesService
    {
        private static readonly JsonSerializerOptions _opcionesJson = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly string _rutaArchivo;
        private readonly SemaphoreSlim _cerrojo = new(1, 1);

        public AlmacenConexionesService()
            : this(Trabajo.RutasAplicacion.Archivo("conexiones.json"))
        {
        }

        public AlmacenConexionesService(string rutaArchivo)
        {
            _rutaArchivo = rutaArchivo;
        }

        public async Task<List<ConexionModelo>> ListaAsync()
        {
            await _cerrojo.WaitAsync();
            try
            {
                return await LeerAsync();
            }
            finally
            {
                _cerrojo.Release();
            }
        }

        public async Task GuardarAsync(ConexionModelo conexion, string? contrasenaNueva)
        {
            if (!string.IsNullOrEmpty(contrasenaNueva))
            {
                conexion.ContrasenaCifrada = Cifrar(contrasenaNueva);
            }

            if (conexion.Autenticacion == TipoAutenticacion.Windows)
            {
                conexion.Usuario = null;
                conexion.ContrasenaCifrada = null;
            }

            await _cerrojo.WaitAsync();
            try
            {
                var conexiones = await LeerAsync();
                var indice = conexiones.FindIndex(c => c.Id == conexion.Id);

                if (indice >= 0)
                {
                    conexiones[indice] = conexion;
                }
                else
                {
                    conexiones.Add(conexion);
                }

                await EscribirAsync(conexiones);
            }
            finally
            {
                _cerrojo.Release();
            }
        }

        public async Task EliminarAsync(Guid id)
        {
            await _cerrojo.WaitAsync();
            try
            {
                var conexiones = await LeerAsync();
                conexiones.RemoveAll(c => c.Id == id);
                await EscribirAsync(conexiones);
            }
            finally
            {
                _cerrojo.Release();
            }
        }

        public static string? ObtenerContrasena(ConexionModelo conexion)
        {
            if (string.IsNullOrEmpty(conexion.ContrasenaCifrada))
            {
                return null;
            }

            var datos = ProtectedData.Unprotect(
                Convert.FromBase64String(conexion.ContrasenaCifrada),
                null,
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(datos);
        }

        private static string Cifrar(string texto)
        {
            var datos = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(texto),
                null,
                DataProtectionScope.CurrentUser);

            return Convert.ToBase64String(datos);
        }

        private async Task<List<ConexionModelo>> LeerAsync()
        {
            if (!File.Exists(_rutaArchivo))
            {
                return [];
            }

            await using var flujo = File.OpenRead(_rutaArchivo);
            var conexiones = await JsonSerializer.DeserializeAsync<List<ConexionModelo>>(flujo, _opcionesJson);

            return conexiones?.OrderBy(c => c.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList() ?? [];
        }

        private async Task EscribirAsync(List<ConexionModelo> conexiones)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_rutaArchivo)!);

            await using var flujo = File.Create(_rutaArchivo);
            await JsonSerializer.SerializeAsync(flujo, conexiones, _opcionesJson);
        }
    }
}
