using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiGestorBDAccesible.Core.Trabajo
{
    public class AlmacenJson<T>(string rutaArchivo) where T : new()
    {
        private static readonly JsonSerializerOptions _opciones = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly SemaphoreSlim _cerrojo = new(1, 1);

        public async Task<T> LeerAsync()
        {
            await _cerrojo.WaitAsync();
            try
            {
                if (!File.Exists(rutaArchivo))
                {
                    return new T();
                }

                await using var flujo = File.OpenRead(rutaArchivo);
                return await JsonSerializer.DeserializeAsync<T>(flujo, _opciones) ?? new T();
            }
            catch (JsonException)
            {
                return new T();
            }
            finally
            {
                _cerrojo.Release();
            }
        }

        public async Task EscribirAsync(T valor)
        {
            await _cerrojo.WaitAsync();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(rutaArchivo)!);
                await using var flujo = File.Create(rutaArchivo);
                await JsonSerializer.SerializeAsync(flujo, valor, _opciones);
            }
            finally
            {
                _cerrojo.Release();
            }
        }
    }
}
