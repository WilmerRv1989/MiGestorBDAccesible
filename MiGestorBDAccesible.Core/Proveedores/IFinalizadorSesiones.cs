using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Ejecucion;
using MiGestorBDAccesible.Core.Edicion;

namespace MiGestorBDAccesible.Core.Proveedores
{
    public interface IFinalizadorSesiones
    {
        TipoMotor Motor { get; }

        ContextoCierre Contexto => ContextoCierre.Actual;

        bool PuedeConfirmar(ConexionActiva? conexion = null) => true;

        bool TieneCambiosPendientes(ConexionActiva? conexion = null) => false;

        Task<ResultadoFinalizacionModelo> FinalizarSesionAsync(ConexionActiva? conexion, bool confirmar) =>
            Task.FromResult(new ResultadoFinalizacionModelo());

        Task DescartarSesionesAsync(ConexionActiva? conexion) => Task.CompletedTask;
    }
}
