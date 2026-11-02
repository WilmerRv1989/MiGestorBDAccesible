namespace MiGestorBDAccesible.Core.Trabajo
{
    public class EntradaHistorialModelo
    {
        public DateTime Fecha { get; init; }

        public Guid ConexionId { get; init; }

        public string? BaseDatos { get; init; }

        public string Texto { get; init; } = "";

        public string Resumen { get; init; } = "";
    }

    public class HistorialConsultasService(string rutaArchivo)
    {
        private const int EntradasMaximas = 200;

        private readonly AlmacenJson<List<EntradaHistorialModelo>> _almacen = new(rutaArchivo);

        public HistorialConsultasService() : this(RutasAplicacion.Archivo("historial.json"))
        {
        }

        public async Task<List<EntradaHistorialModelo>> ListaAsync(Guid conexionId) =>
            (await _almacen.LeerAsync()).Where(e => e.ConexionId == conexionId).ToList();

        public async Task AgregarAsync(EntradaHistorialModelo entrada)
        {
            var entradas = await _almacen.LeerAsync();
            entradas.RemoveAll(e => e.ConexionId == entrada.ConexionId && e.Texto == entrada.Texto && e.BaseDatos == entrada.BaseDatos);
            entradas.Insert(0, entrada);

            if (entradas.Count > EntradasMaximas)
            {
                entradas.RemoveRange(EntradasMaximas, entradas.Count - EntradasMaximas);
            }

            await _almacen.EscribirAsync(entradas);
        }

        public async Task BorrarAsync(Guid conexionId)
        {
            var entradas = await _almacen.LeerAsync();
            entradas.RemoveAll(e => e.ConexionId == conexionId);
            await _almacen.EscribirAsync(entradas);
        }
    }
}
