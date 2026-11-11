using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Ejecucion;
using MiGestorBDAccesible.Core.Estructura;

namespace MiGestorBDAccesible.Core.Proveedores
{
    public interface IProveedorBaseDatos : IFinalizadorSesiones
    {
        CapacidadesMotorModelo Capacidades { get; }

        string EsquemaPredeterminado(ConexionActiva conexion);

        Task<ResultadoPruebaConexion> ProbarConexionAsync(
            ConexionModelo conexion,
            string? contrasena,
            CancellationToken cancelacion = default);

        Task<List<BaseDatosModelo>> ObtenerBasesDatosAsync(
            ConexionActiva conexion,
            CancellationToken cancelacion = default);

        Task<List<TablaModelo>> ObtenerTablasAsync(
            ConexionActiva conexion,
            string baseDatos,
            CancellationToken cancelacion = default);

        Task<DetalleTablaModelo> ObtenerDetalleTablaAsync(
            ConexionActiva conexion,
            string baseDatos,
            TablaModelo tabla,
            CancellationToken cancelacion = default);

        string BaseDatosAdministracion { get; }

        Task<string> GenerarScriptAsync(
            ConexionActiva conexion,
            string baseDatos,
            TablaModelo tabla,
            TipoScript tipo,
            CancellationToken cancelacion = default);

        string ScriptCrearBaseDatos(string nombre);

        string ScriptEliminarBaseDatos(string nombre);

        string ScriptEliminarTabla(TablaModelo tabla);

        string ScriptEliminarTabla(TablaModelo tabla, bool definitivo) => ScriptEliminarTabla(tabla);

        IReadOnlyList<TipoDatosModelo> TiposDatos { get; }

        string ScriptCrearTabla(DefinicionTablaModelo tabla);

        Task<List<DestinoLlaveModelo>> ObtenerDestinosLlaveAsync(
            ConexionActiva conexion,
            string baseDatos,
            CancellationToken cancelacion = default);

        string ScriptCrearLlaveForanea(DefinicionLlaveForaneaModelo llave);

        void ReiniciarConexiones();

        Task<ResultadoEjecucionModelo> EjecutarAsync(
            ConexionActiva conexion,
            string? baseDatos,
            string script,
            int lineaInicial,
            CancellationToken cancelacion = default);

        Task<Dictionary<TipoObjeto, int>> ContarObjetosAsync(
            ConexionActiva conexion,
            string baseDatos,
            CancellationToken cancelacion = default);

        Task<List<ObjetoBaseDatosModelo>> ObtenerObjetosAsync(
            ConexionActiva conexion,
            string baseDatos,
            TipoObjeto tipo,
            CancellationToken cancelacion = default);

        Task<List<ObjetoBaseDatosModelo>> ObtenerDisparadoresTablaAsync(
            ConexionActiva conexion,
            string baseDatos,
            TablaModelo tabla,
            CancellationToken cancelacion = default);

        Task<DetalleObjetoModelo> ObtenerDetalleObjetoAsync(
            ConexionActiva conexion,
            string baseDatos,
            ObjetoBaseDatosModelo objeto,
            CancellationToken cancelacion = default);

        Task<string> GenerarScriptObjetoAsync(
            ConexionActiva conexion,
            string baseDatos,
            ObjetoBaseDatosModelo objeto,
            CancellationToken cancelacion = default);

        Task<DependenciasModelo> ObtenerDependenciasAsync(
            ConexionActiva conexion,
            string baseDatos,
            ResultadoBusquedaObjetoModelo objetivo,
            CancellationToken cancelacion = default);

        Task<List<ResultadoBusquedaObjetoModelo>> BuscarObjetosAsync(
            ConexionActiva conexion,
            string baseDatos,
            string texto,
            CategoriaBusqueda categoria,
            CancellationToken cancelacion = default);

        string ScriptEliminarObjeto(ObjetoBaseDatosModelo objeto);

        string ScriptHabilitarDisparador(ObjetoBaseDatosModelo disparador, bool habilitar);

        string ScriptReiniciarSecuencia(ObjetoBaseDatosModelo secuencia, long valor);

        string ScriptLlamada(DetalleObjetoModelo detalle, IReadOnlyDictionary<string, string?> valores);

        string ScriptSeleccionarPrimeras(DetalleObjetoModelo vista);

        DialectoSqlModelo Dialecto { get; }

        Task<Edicion.ISesionEdicion> AbrirSesionEdicionAsync(ConexionActiva conexion, string baseDatos, CancellationToken cancelacion = default);

        string ScriptCrearIndice(DefinicionIndiceModelo indice);

        string ScriptEliminarIndice(TablaModelo tabla, IndiceModelo indice);

        string ScriptCrearCheck(DefinicionCheckModelo check);

        string ScriptEliminarCheck(TablaModelo tabla, string nombre);

        ScriptModificacionModelo ScriptModificarTabla(PlanModificacionModelo plan);

        Task<ResultadoModificacionModelo> AplicarModificacionAsync(
            ConexionActiva conexion,
            string baseDatos,
            ScriptModificacionModelo script,
            CancellationToken cancelacion = default);

        Task<string> ScriptRecrearTablaAsync(
            ConexionActiva conexion,
            string baseDatos,
            PlanModificacionModelo plan,
            CancellationToken cancelacion = default);

        Task<Autocompletado.CatalogoModelo> ObtenerCatalogoAsync(ConexionActiva conexion, string? baseDatos, CancellationToken cancelacion = default);

        string NombreGuardado(string nombre) => nombre;

        string? AvisoNombre(string nombre) => null;

    }
}
