using System.Data.Common;

namespace MiGestorBDAccesible.Core.Autocompletado
{
    public enum TipoObjetoCatalogo
    {
        Tabla,
        Vista,
        Procedimiento,
        Funcion
    }

    public record ColumnaCatalogoModelo(string Nombre, string Tipo);

    public record ObjetoCatalogoModelo(TipoObjetoCatalogo Tipo, string Esquema, string Nombre, List<ColumnaCatalogoModelo> Columnas);

    public class CatalogoModelo
    {
        public string EsquemaPredeterminado { get; init; } = "";

        public List<ObjetoCatalogoModelo> Objetos { get; init; } = [];

        public static async Task<CatalogoModelo> LeerAsync(
            DbDataReader lector,
            string esquemaPredeterminado,
            Func<DbDataReader, string> tipoColumna,
            CancellationToken cancelacion)
        {
            var objetos = new List<ObjetoCatalogoModelo>();
            var porClave = new Dictionary<(TipoObjetoCatalogo, string, string), ObjetoCatalogoModelo>();

            while (await lector.ReadAsync(cancelacion))
            {
                var tipo = (TipoObjetoCatalogo)Convert.ToInt32(lector.GetValue(0));
                var clave = (tipo, lector.IsDBNull(1) ? "" : lector.GetString(1), lector.GetString(2));

                if (!porClave.TryGetValue(clave, out var objeto))
                {
                    objeto = new ObjetoCatalogoModelo(tipo, clave.Item2, clave.Item3, []);
                    porClave[clave] = objeto;
                    objetos.Add(objeto);
                }

                if (!lector.IsDBNull(3))
                {
                    objeto.Columnas.Add(new ColumnaCatalogoModelo(lector.GetString(3), tipoColumna(lector)));
                }
            }

            return new CatalogoModelo { EsquemaPredeterminado = esquemaPredeterminado, Objetos = objetos };
        }
    }
}
