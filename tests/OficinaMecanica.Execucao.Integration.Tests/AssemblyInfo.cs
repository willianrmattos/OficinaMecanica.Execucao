// Os contadores do Meter sao estaticos e os testes de infra compartilham containers: sem paralelismo entre
// classes de teste, cada medicao enxerga so o que o proprio teste produziu.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
