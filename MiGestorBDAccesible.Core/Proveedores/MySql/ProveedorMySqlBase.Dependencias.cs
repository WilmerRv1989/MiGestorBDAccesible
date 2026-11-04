using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Estructura;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Proveedores.MySql
{
    public abstract partial class ProveedorMySqlBase
    {
        public sealed record ObjetoConTexto(TipoResultadoBusqueda Tipo, string Nombre, string? Tabla, string Texto);

        private const string ConsultaObjetosConTexto = """
            SELECT 0 tipo, table_name nombre, NULL tabla, '' texto FROM information_schema.tables WHERE table_schema = @base AND table_type = 'BASE TABLE'
            UNION ALL
            SELECT 2, table_name, NULL, COALESCE(view_definition, '') FROM information_schema.views WHERE table_schema = @base
            UNION ALL
            SELECT CASE routine_type WHEN 'PROCEDURE' THEN 3 ELSE 4 END, routine_name, NULL, COALESCE(routine_definition, '')
            FROM information_schema.routines WHERE routine_schema = @base
            UNION ALL
            SELECT 12, trigger_name, event_object_table, COALESCE(action_statement, '') FROM information_schema.triggers WHERE trigger_schema = @base
            UNION ALL
            SELECT 13, table_name, NULL, '' FROM information_schema.tables WHERE table_schema = @base AND table_type = 'SEQUENCE'
            """;

        public async Task<DependenciasModelo> ObtenerDependenciasAsync(
            ConexionActiva conexion,
            string baseDatos,
            ResultadoBusquedaObjetoModelo objetivo,
            CancellationToken cancelacion = default)
        {
            await using var conexionMy = await AbrirAsync(conexion, cancelacion);
            var objetos = await LeerAsync(conexionMy, ConsultaObjetosConTexto, baseDatos, "", cancelacion, lector => new ObjetoConTexto(
                (TipoResultadoBusqueda)Convert.ToInt32(lector.GetValue(0)),
                lector.GetString(1),
                lector.IsDBNull(2) ? null : lector.GetString(2),
                lector.GetString(3)));

            return CalcularDependencias(objetivo, objetos);
        }

        public static DependenciasModelo CalcularDependencias(ResultadoBusquedaObjetoModelo objetivo, List<ObjetoConTexto> objetos)
        {
            var dependencias = new DependenciasModelo();
            var propio = objetos.FirstOrDefault(o => o.Tipo == objetivo.Tipo && o.Nombre.Equals(objetivo.Nombre, StringComparison.OrdinalIgnoreCase));

            foreach (var otro in objetos.Where(o => !ReferenceEquals(o, propio)))
            {
                if (otro.Tipo == TipoResultadoBusqueda.Disparador && otro.Tabla is { } tabla
                    && objetivo.Tipo == TipoResultadoBusqueda.Tabla && tabla.Equals(objetivo.Nombre, StringComparison.OrdinalIgnoreCase))
                {
                    dependencias.UsadoPor.Add(Dependencia(otro, false));
                }
                else if (Menciona(otro.Texto, objetivo.Nombre))
                {
                    dependencias.UsadoPor.Add(Dependencia(otro, otro.Tipo != TipoResultadoBusqueda.Vista));
                }

                if (propio is null)
                {
                    continue;
                }

                if (propio.Tipo == TipoResultadoBusqueda.Disparador && otro.Tipo == TipoResultadoBusqueda.Tabla
                    && otro.Nombre.Equals(propio.Tabla, StringComparison.OrdinalIgnoreCase))
                {
                    dependencias.Usa.Add(Dependencia(otro, false));
                }
                else if (otro.Tipo != TipoResultadoBusqueda.Disparador && Menciona(propio.Texto, otro.Nombre))
                {
                    dependencias.Usa.Add(Dependencia(otro, propio.Tipo != TipoResultadoBusqueda.Vista));
                }
            }

            return dependencias;
        }

        private static bool Menciona(string texto, string nombre) =>
            texto.Length > 0 && Regex.IsMatch(texto, $@"(?<![\w$]){Regex.Escape(nombre)}(?![\w$])", RegexOptions.IgnoreCase);

        private static DependenciaModelo Dependencia(ObjetoConTexto objeto, bool posible) => new()
        {
            Objeto = new ResultadoBusquedaObjetoModelo
            {
                Tipo = objeto.Tipo,
                Esquema = "",
                Nombre = objeto.Nombre,
                Tabla = objeto.Tabla is null ? null : new TablaModelo { Esquema = "", Nombre = objeto.Tabla, EsquemaPredeterminado = "" }
            },
            Posible = posible
        };
    }
}
