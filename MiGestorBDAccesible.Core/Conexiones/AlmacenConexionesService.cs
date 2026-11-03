using System.Globalization;
using System.Runtime.ExceptionServices;
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
        private readonly TimeProvider _reloj;
        private readonly SemaphoreSlim _cerrojo = new(1, 1);

        public AlmacenConexionesService()
            : this(Trabajo.RutasAplicacion.Archivo("conexiones.json"))
        {
        }

        public AlmacenConexionesService(string rutaArchivo)
            : this(rutaArchivo, TimeProvider.System)
        {
        }

        public AlmacenConexionesService(string rutaArchivo, TimeProvider reloj)
        {
            _rutaArchivo = rutaArchivo;
            _reloj = reloj;
        }

        public async Task<ResultadoCargaConexiones> CargarAsync()
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

        public async Task<List<ConexionModelo>> ListaAsync() => (await CargarAsync()).Conexiones;

        public static string? DescribirCarga(ResultadoCargaConexiones resultado)
        {
            var avisoDescartadas = resultado.Descartadas == 1
                ? "Se descartó 1 conexión que no se pudo leer."
                : $"Se descartaron {resultado.Descartadas} conexiones que no se pudieron leer.";
            var aviso = resultado.Estado switch
            {
                EstadoCargaConexiones.Danado => "El archivo conexiones.json estaba dañado. La lista de conexiones está vacía.",
                EstadoCargaConexiones.Parcial => $"{avisoDescartadas} Se conservaron las conexiones legibles.",
                EstadoCargaConexiones.ErrorLectura when resultado.Descartadas > 0 => $"Se leyeron las conexiones legibles. {avisoDescartadas} No se pudo conservar o reescribir conexiones.json. {resultado.Error?.Message} No puedes guardar ni eliminar conexiones hasta resolver el error.",
                EstadoCargaConexiones.ErrorLectura => $"No se pudo leer conexiones.json. No puedes guardar ni eliminar conexiones hasta resolver el error. {resultado.Error?.Message}",
                _ => null
            };

            if (aviso is not null && resultado.RutaCopia is not null)
            {
                aviso += $" El archivo original se conservó como {Path.GetFileName(resultado.RutaCopia)} en la carpeta {Path.GetDirectoryName(Path.GetFullPath(resultado.RutaCopia))}.";
            }

            return aviso;
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
                var conexiones = await LeerParaModificarAsync();
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
                var conexiones = await LeerParaModificarAsync();
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

        private async Task<List<ConexionModelo>> LeerParaModificarAsync()
        {
            var resultado = await LeerAsync();

            if (resultado.Error is not null)
            {
                ExceptionDispatchInfo.Capture(resultado.Error).Throw();
            }

            return resultado.Conexiones;
        }

        private async Task<ResultadoCargaConexiones> LeerAsync()
        {
            string? rutaCopia = null;
            var conexiones = new List<ConexionModelo>();
            var descartadas = 0;
            try
            {
                JsonDocument documento;
                try
                {
                    await using var flujo = File.OpenRead(_rutaArchivo);
                    documento = await JsonDocument.ParseAsync(flujo);
                }
                catch (FileNotFoundException)
                {
                    return new();
                }
                catch (DirectoryNotFoundException)
                {
                    return new();
                }
                catch (JsonException)
                {
                    rutaCopia = ConservarOriginal(mover: true);
                    return new() { Estado = EstadoCargaConexiones.Danado, RutaCopia = rutaCopia };
                }

                using (documento)
                {
                    if (documento.RootElement.ValueKind != JsonValueKind.Array)
                    {
                        rutaCopia = ConservarOriginal(mover: true);
                        return new() { Estado = EstadoCargaConexiones.Danado, RutaCopia = rutaCopia };
                    }

                    foreach (var elemento in documento.RootElement.EnumerateArray())
                    {
                        if (elemento.ValueKind == JsonValueKind.Object)
                        {
                            try
                            {
                                var conexion = elemento.Deserialize<ConexionModelo>(_opcionesJson);
                                if (conexion is not null && Enum.IsDefined(conexion.Motor) && Enum.IsDefined(conexion.Autenticacion))
                                {
                                    conexiones.Add(conexion);
                                    continue;
                                }
                            }
                            catch (JsonException)
                            {
                            }
                        }

                        descartadas++;
                    }

                    conexiones = conexiones.OrderBy(c => c.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList();
                    if (descartadas > 0)
                    {
                        rutaCopia = ConservarOriginal(mover: false);
                        await EscribirAsync(conexiones);
                    }

                    return new()
                    {
                        Conexiones = conexiones,
                        Estado = descartadas > 0 ? EstadoCargaConexiones.Parcial : EstadoCargaConexiones.Correcta,
                        Descartadas = descartadas,
                        RutaCopia = rutaCopia
                    };
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return new()
                {
                    Conexiones = conexiones,
                    Estado = EstadoCargaConexiones.ErrorLectura,
                    Descartadas = descartadas,
                    Error = error,
                    RutaCopia = rutaCopia
                };
            }
        }

        private string ConservarOriginal(bool mover)
        {
            var prefijo = $"{_rutaArchivo}.danado-{_reloj.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";
            var rutaCopia = prefijo;
            for (var numero = 2; File.Exists(rutaCopia); numero++)
            {
                rutaCopia = $"{prefijo}-{numero}";
            }

            if (mover)
            {
                File.Move(_rutaArchivo, rutaCopia);
            }
            else
            {
                File.Copy(_rutaArchivo, rutaCopia);
            }

            return rutaCopia;
        }

        protected virtual Stream AbrirParaEscribir(string ruta)
        {
            return new FileStream(ruta, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }

        private async Task EscribirAsync(List<ConexionModelo> conexiones)
        {
            var carpeta = Path.GetDirectoryName(Path.GetFullPath(_rutaArchivo))!;
            Directory.CreateDirectory(carpeta);
            var temporal = Path.Combine(carpeta, $".{Path.GetFileName(_rutaArchivo)}-{Guid.NewGuid():N}.tmp");

            try
            {
                await using (var flujo = AbrirParaEscribir(temporal))
                {
                    await JsonSerializer.SerializeAsync(flujo, conexiones, _opcionesJson);
                    await flujo.FlushAsync();
                }

                File.Move(temporal, _rutaArchivo, overwrite: true);
            }
            finally
            {
                try
                {
                    File.Delete(temporal);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // La limpieza no debe ocultar el error original de escritura.
                }
            }
        }
    }
}
