namespace MiGestorBDAccesible.Core.Trabajo
{
    public class SesionEditorModelo
    {
        public Guid ConexionId { get; set; }

        public string? BaseDatos { get; set; }

        public string Texto { get; set; } = "";

        public string? Archivo { get; set; }

        public string TextoGuardado { get; set; } = "";

        public List<PestanaGuardadaModelo> Pestanas { get; set; } = [];

        public int Activa { get; set; }
    }

    public class PestanaGuardadaModelo
    {
        public int Numero { get; set; }

        public string? Titulo { get; set; }

        public string? BaseDatos { get; set; }

        public string Texto { get; set; } = "";

        public string? Archivo { get; set; }

        public string TextoGuardado { get; set; } = "";
    }

    public class SesionEditorService(string rutaArchivo)
    {
        private readonly AlmacenJson<Dictionary<Guid, SesionEditorModelo>> _almacen = new(rutaArchivo);

        public SesionEditorService() : this(RutasAplicacion.Archivo("sesiones.json"))
        {
        }

        public async Task<SesionEditorModelo?> ObtenerAsync(Guid conexionId) =>
            (await _almacen.LeerAsync()).GetValueOrDefault(conexionId);

        public async Task GuardarAsync(SesionEditorModelo sesion)
        {
            var sesiones = await _almacen.LeerAsync();
            sesiones[sesion.ConexionId] = sesion;
            await _almacen.EscribirAsync(sesiones);
        }
    }
}
