using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Descripcion;
using MiGestorBDAccesible.Core.Proveedores;

namespace MiGestorBDAccesible.Core.Estructura
{
    public static class DependenciasService
    {
        public static ResultadoBusquedaObjetoModelo DesdeTabla(TablaModelo tabla) =>
            new() { Tipo = TipoResultadoBusqueda.Tabla, Esquema = tabla.Esquema, Nombre = tabla.Nombre };

        public static ResultadoBusquedaObjetoModelo DesdeObjeto(ObjetoBaseDatosModelo objeto) => new()
        {
            Tipo = objeto.Tipo switch
            {
                TipoObjeto.Vista => TipoResultadoBusqueda.Vista,
                TipoObjeto.Procedimiento => TipoResultadoBusqueda.Procedimiento,
                TipoObjeto.Funcion => TipoResultadoBusqueda.Funcion,
                TipoObjeto.Paquete => TipoResultadoBusqueda.Paquete,
                TipoObjeto.Secuencia => TipoResultadoBusqueda.Secuencia,
                _ => TipoResultadoBusqueda.Disparador
            },
            Esquema = objeto.Esquema,
            Nombre = objeto.Nombre,
            Tabla = objeto.Tabla,
            Detalle = objeto.Firma,
            Id = objeto.Id
        };

        public static async Task<DependenciasModelo> LeerAsync(
            IProveedorBaseDatos proveedor,
            ConexionActiva conexion,
            string baseDatos,
            ResultadoBusquedaObjetoModelo objetivo,
            CancellationToken cancelacion = default)
        {
            var dependencias = await proveedor.ObtenerDependenciasAsync(conexion, baseDatos, objetivo, cancelacion);

            if (objetivo.Tipo != TipoResultadoBusqueda.Tabla)
            {
                return Ordenar(dependencias);
            }

            var tabla = new TablaModelo { Esquema = objetivo.Esquema, Nombre = objetivo.Nombre };
            var detalle = await proveedor.ObtenerDetalleTablaAsync(conexion, baseDatos, tabla, cancelacion);

            dependencias.Usa.AddRange(detalle.LlavesForaneas
                .Where(l => l.TablaDestino.NombreCompleto != detalle.Tabla.NombreCompleto)
                .Select(l => new DependenciaModelo { Objeto = DesdeTabla(l.TablaDestino), Nota = $"por la llave {l.Nombre}" }));

            dependencias.UsadoPor.AddRange(detalle.Referencias
                .Where(r => r.TablaOrigen.NombreCompleto != detalle.Tabla.NombreCompleto)
                .Select(r => new DependenciaModelo { Objeto = DesdeTabla(r.TablaOrigen), Nota = $"por la llave {r.Nombre}" }));

            return Ordenar(dependencias);
        }

        public static async Task<List<DependenciaModelo>> ObjetosQueUsanAsync(
            IProveedorBaseDatos proveedor,
            ConexionActiva conexion,
            string baseDatos,
            ResultadoBusquedaObjetoModelo objetivo,
            CancellationToken cancelacion = default)
        {
            var dependencias = await proveedor.ObtenerDependenciasAsync(conexion, baseDatos, objetivo, cancelacion);
            return Ordenar(dependencias).UsadoPor.Where(d => d.Objeto?.Tipo != TipoResultadoBusqueda.Tabla).ToList();
        }

        private static DependenciasModelo Ordenar(DependenciasModelo dependencias)
        {
            static List<DependenciaModelo> Lista(IEnumerable<DependenciaModelo> lista) => lista
                .GroupBy(d => d.Objeto is { } o ? $"{(int)o.Tipo}|{o.NombreCompleto}|{o.Tabla?.NombreCompleto}|{o.Detalle}|{d.Nota}" : d.Inexistente!, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(d => d.Posible).First())
                .OrderBy(d => d.Objeto is null)
                .ThenBy(d => d.Posible)
                .ThenBy(d => d.Objeto?.Tipo)
                .ThenBy(d => d.Objeto?.NombreCompleto, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new DependenciasModelo { Usa = Lista(dependencias.Usa), UsadoPor = Lista(dependencias.UsadoPor) };
        }

        public static string Describir(DependenciaModelo dependencia, string disparadoresBase = "de la base")
        {
            if (dependencia.Objeto is null)
            {
                return $"Nombra a {dependencia.Inexistente}, que no existe";
            }

            var texto = BuscadorObjetosService.Describir(dependencia.Objeto, disparadoresBase);

            if (dependencia.Nota is not null)
            {
                texto += $", {dependencia.Nota}";
            }

            if (dependencia.Posible)
            {
                texto += " (posible uso: el nombre aparece en el código de la función)";
            }

            return texto;
        }

        public static string ConArticulo(DependenciaModelo dependencia)
        {
            var objeto = dependencia.Objeto!;
            var articulo = objeto.Tipo switch
            {
                TipoResultadoBusqueda.Tabla => "la tabla",
                TipoResultadoBusqueda.Vista => "la vista",
                TipoResultadoBusqueda.Procedimiento => "el procedimiento",
                TipoResultadoBusqueda.Funcion => "la función",
                TipoResultadoBusqueda.Paquete => "el paquete",
                TipoResultadoBusqueda.Disparador => "el disparador",
                TipoResultadoBusqueda.Secuencia => "la secuencia",
                _ => "el objeto"
            };

            return $"{articulo} {objeto.NombreCompleto}{(dependencia.Posible ? " (posible)" : "")}";
        }

        public static string? AvisoEliminar(List<DependenciaModelo> usanObjeto, string queSeBorra)
        {
            var objetos = usanObjeto.Where(d => d.Objeto is not null).ToList();

            if (objetos.Count == 0)
            {
                return null;
            }

            var verbo = objetos.Count == 1 ? "usa" : "usan";
            return $"Dejarán de funcionar: {NarradorTablaService.Unir(objetos.Select(ConArticulo))} {verbo} {queSeBorra}.";
        }

        public static bool ModificacionAfecta(IEnumerable<Definicion.TipoCambioTabla> cambios, bool recrea) =>
            recrea || cambios.Any(c => c is Definicion.TipoCambioTabla.QuitarColumna
                or Definicion.TipoCambioTabla.RenombrarColumna
                or Definicion.TipoCambioTabla.CambiarTipo
                or Definicion.TipoCambioTabla.RenombrarTabla);

        public static string AvisoModificar(List<DependenciaModelo> usanTabla, TipoMotor motor)
        {
            var lista = NarradorTablaService.Unir(usanTabla.Select(ConArticulo));
            var verbo = usanTabla.Count == 1 ? "usa" : "usan";
            var consecuencia = motor switch
            {
                TipoMotor.Oracle => "Oracle los marcará como inválidos y los recompila solo la próxima vez que se usen. Si usan una columna que ya no existe, quedarán con errores.",
                TipoMotor.PostgreSql => "PostgreSQL no deja quitar ni cambiar el tipo de una columna que use una vista. Renombrar sí: la vista se actualiza sola.",
                _ => "Si usan una columna que quitas o renombras, fallarán al ejecutarse."
            };

            return $"{char.ToUpper(lista[0])}{lista[1..]} {verbo} esta tabla. {consecuencia}";
        }
    }
}
