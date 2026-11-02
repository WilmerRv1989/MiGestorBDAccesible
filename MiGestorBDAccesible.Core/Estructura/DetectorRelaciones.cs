namespace MiGestorBDAccesible.Core.Estructura
{
    public static class DetectorRelaciones
    {
        public static List<RelacionMuchosAMuchosModelo> BuscarMuchosAMuchos<TId>(
            TId idTabla,
            List<(LlaveForaneaModelo Llave, TId Origen, TId Destino)> llaves,
            Dictionary<TId, HashSet<string>> clavesReferenciantes)
            where TId : notnull
        {
            var comparador = EqualityComparer<TId>.Default;
            var relaciones = new List<RelacionMuchosAMuchosModelo>();

            foreach (var (idIntermedia, clave) in clavesReferenciantes)
            {
                if (comparador.Equals(idIntermedia, idTabla) || clave.Count < 2)
                {
                    continue;
                }

                var propias = llaves.Where(l => comparador.Equals(l.Origen, idIntermedia)).ToList();
                var columnasLlaves = propias.SelectMany(l => l.Llave.ColumnasOrigen);

                if (propias.Count < 2 || !clave.SetEquals(columnasLlaves))
                {
                    continue;
                }

                var otra = propias.FirstOrDefault(l => !comparador.Equals(l.Destino, idTabla));

                if (otra.Llave is null)
                {
                    continue;
                }

                relaciones.Add(new RelacionMuchosAMuchosModelo
                {
                    TablaIntermedia = otra.Llave.TablaOrigen,
                    OtraTabla = otra.Llave.TablaDestino
                });
            }

            return relaciones;
        }
    }
}
