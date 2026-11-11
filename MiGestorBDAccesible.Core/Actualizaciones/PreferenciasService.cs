using System.Text.Json;

namespace MiGestorBDAccesible.Core.Actualizaciones
{
    public class PreferenciasModelo
    {
        public bool BuscarAlIniciar { get; set; } = true;

        public DateTimeOffset? UltimaBusqueda { get; set; }

        public string? VersionOmitida { get; set; }

        public bool SugerirAlEscribir { get; set; } = true;
    }

    public class PreferenciasService
    {
        private static readonly JsonSerializerOptions _opciones = new() { WriteIndented = true };

        private readonly string _ruta;
        private readonly SemaphoreSlim _cerrojo = new(1, 1);

        public PreferenciasService() : this(Trabajo.RutasAplicacion.Archivo("preferencias.json"))
        {
        }

        public PreferenciasService(string ruta)
        {
            _ruta = ruta;
        }

        public async Task<PreferenciasModelo> LeerAsync()
        {
            await _cerrojo.WaitAsync();
            try
            {
                return await LeerSinCerrojoAsync();
            }
            finally
            {
                _cerrojo.Release();
            }
        }

        public async Task CambiarAsync(Action<PreferenciasModelo> cambio)
        {
            await _cerrojo.WaitAsync();
            try
            {
                var preferencias = await LeerSinCerrojoAsync();
                cambio(preferencias);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_ruta))!);
                var temporal = _ruta + ".tmp";
                await File.WriteAllTextAsync(temporal, JsonSerializer.Serialize(preferencias, _opciones));
                File.Move(temporal, _ruta, overwrite: true);
            }
            finally
            {
                _cerrojo.Release();
            }
        }

        private async Task<PreferenciasModelo> LeerSinCerrojoAsync()
        {
            try
            {
                var json = await File.ReadAllTextAsync(_ruta);
                return JsonSerializer.Deserialize<PreferenciasModelo>(json, _opciones) ?? new PreferenciasModelo();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            {
                return new PreferenciasModelo();
            }
        }
    }
}
