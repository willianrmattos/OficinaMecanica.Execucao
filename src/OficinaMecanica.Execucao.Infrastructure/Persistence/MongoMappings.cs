using System.Reflection;
using System.Runtime.CompilerServices;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using OficinaMecanica.Execucao.Domain.Common;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.ValueObjects;

namespace OficinaMecanica.Execucao.Infrastructure.Persistence;

// Optei por mapear com BsonClassMap aqui, na Infrastructure, para o Domain continuar sem referencia ao
// driver. O registro e global e congela no primeiro uso do driver, entao precisa rodar uma unica vez e
// antes de qualquer leitura ou escrita (o startup chama Registrar antes de criar o MongoClient).
public static class MongoMappings
{
    private static readonly object Trava = new();
    private static bool _registrado;

    public const string ColecaoDeOrdens = "ordens_producao";

    // Ordem e tombstone moram na MESMA colecao com o MESMO _id (= OrdemDeServicoId): e isso que faz o
    // Enfileirar e o CancelarProducao concorrentes colidirem na chave unica. O discriminador separa os dois
    // para que leituras, filas e listagens enxerguem so as ordens.
    public const string TipoOrdem = "ordem";
    public const string TipoCancelamentoPrevio = "cancelamentoPrevio";
    public const string CampoDiscriminador = "_t";

    public static void Registrar()
    {
        lock (Trava)
        {
            if (_registrado) return;

            // O driver 3.x nao tem GuidRepresentation padrao: sem isto, serializar um Guid lanca excecao.
            // Standard (UUID subtype 4) e o formato interoperavel entre drivers.
            BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

            // Data BSON (UTC, milissegundos) em vez do documento {DateTime, Ticks, Offset} do padrao:
            // so assim os indices e as ordenacoes por data funcionam.
            BsonSerializer.RegisterSerializer(new DateTimeOffsetSerializer(BsonType.DateTime));

            RegistrarBases();
            RegistrarValueObjects();
            RegistrarEtapa();
            RegistrarOrdem();
            RegistrarCancelamentoPrevio();

            _registrado = true;
        }
    }

    // Id e Version sao declarados nas classes base do Domain e o driver so mapeia membros da propria
    // classe, entao as duas bases precisam do proprio mapa.
    private static void RegistrarBases()
    {
        BsonClassMap.RegisterClassMap<Entity>(cm => cm.MapIdMember(x => x.Id));
        BsonClassMap.RegisterClassMap<AggregateRoot>(cm => cm.MapMember(x => x.Version).SetElementName("version"));
    }

    // Os value objects sao imutaveis (so getter) e a reidratacao passa pelo construtor privado, sem
    // revalidar: o que esta gravado ja passou pelas regras do Domain.
    private static void RegistrarValueObjects()
    {
        BsonClassMap.RegisterClassMap<VeiculoDaOrdem>(cm =>
        {
            cm.MapProperty(x => x.Placa).SetElementName("placa");
            cm.MapProperty(x => x.Marca).SetElementName("marca");
            cm.MapProperty(x => x.Modelo).SetElementName("modelo");
            cm.MapConstructor(ConstrutorUnico<VeiculoDaOrdem>(), "Placa", "Marca", "Modelo");
        });

        BsonClassMap.RegisterClassMap<ServicoDaOrdem>(cm =>
        {
            cm.MapProperty(x => x.ServicoId).SetElementName("servicoId");
            cm.MapProperty(x => x.Nome).SetElementName("nome");
            cm.MapProperty(x => x.Quantidade).SetElementName("quantidade");
            cm.MapProperty(x => x.PrecoUnitario).SetElementName("precoUnitario");
            cm.MapConstructor(ConstrutorUnico<ServicoDaOrdem>(), "ServicoId", "Nome", "Quantidade", "PrecoUnitario");
        });

        BsonClassMap.RegisterClassMap<PecaDaOrdem>(cm =>
        {
            cm.MapProperty(x => x.PecaId).SetElementName("pecaId");
            cm.MapProperty(x => x.Nome).SetElementName("nome");
            cm.MapProperty(x => x.Quantidade).SetElementName("quantidade");
            cm.MapProperty(x => x.PrecoUnitario).SetElementName("precoUnitario");
            cm.MapConstructor(ConstrutorUnico<PecaDaOrdem>(), "PecaId", "Nome", "Quantidade", "PrecoUnitario");
        });

        BsonClassMap.RegisterClassMap<Laudo>(cm =>
        {
            cm.MapProperty(x => x.Texto).SetElementName("texto");
            // Elemento ausente (horas nao informadas): sem valor padrao o driver nao acha o construtor.
            cm.MapProperty(x => x.HorasEstimadas).SetElementName("horasEstimadas").SetIgnoreIfNull(true).SetDefaultValue((decimal?)null);
            cm.MapConstructor(ConstrutorUnico<Laudo>(), "Texto", "HorasEstimadas");
        });

        BsonClassMap.RegisterClassMap<Apontamento>(cm =>
        {
            cm.MapProperty(x => x.MecanicoId).SetElementName("mecanicoId");
            cm.MapProperty(x => x.Texto).SetElementName("texto");
            cm.MapProperty(x => x.Em).SetElementName("em");
            cm.MapConstructor(ConstrutorUnico<Apontamento>(), "MecanicoId", "Texto", "Em");
        });
    }

    private static void RegistrarEtapa()
    {
        BsonClassMap.RegisterClassMap<EtapaProducao>(cm =>
        {
            cm.MapProperty(x => x.Nome).SetElementName("nome").SetSerializer(new EnumSerializer<NomeEtapa>(BsonType.String));
            cm.MapProperty(x => x.MecanicoId).SetElementName("mecanicoId");
            cm.MapProperty(x => x.Status).SetElementName("status").SetSerializer(new EnumSerializer<StatusEtapa>(BsonType.String));
            cm.MapProperty(x => x.Inicio).SetElementName("inicio");
            cm.MapProperty(x => x.Fim).SetElementName("fim").SetIgnoreIfNull(true);
            cm.SetCreator(CriarSemConstrutor<EtapaProducao>);
        });
    }

    // O agregado nao tem construtor sem argumentos e as colecoes internas sao campos privados: crio a
    // instancia sem passar pelo construtor e o driver preenche campos e propriedades (setters privados).
    private static void RegistrarOrdem()
    {
        BsonClassMap.RegisterClassMap<OrdemDeProducao>(cm =>
        {
            cm.SetDiscriminator(TipoOrdem);
            cm.SetDiscriminatorIsRequired(true);

            cm.MapProperty(x => x.Numero).SetElementName("numero");
            cm.MapProperty(x => x.Veiculo).SetElementName("veiculo");
            cm.MapProperty(x => x.Observacoes).SetElementName("observacoes").SetIgnoreIfNull(true);
            cm.MapProperty(x => x.Status).SetElementName("status").SetSerializer(new EnumSerializer<StatusProducao>(BsonType.String));
            cm.MapProperty(x => x.MecanicoId).SetElementName("mecanicoId").SetIgnoreIfNull(true);
            cm.MapProperty(x => x.Laudo).SetElementName("laudo").SetIgnoreIfNull(true);
            cm.MapProperty(x => x.Resumo).SetElementName("resumo").SetIgnoreIfNull(true);
            cm.MapProperty(x => x.MotivoInviabilidade).SetElementName("motivoInviabilidade").SetIgnoreIfNull(true);
            cm.MapProperty(x => x.InviabilizadaPor).SetElementName("inviabilizadaPor").SetIgnoreIfNull(true);
            cm.MapProperty(x => x.MotivoCancelamento).SetElementName("motivoCancelamento").SetIgnoreIfNull(true);
            cm.MapProperty(x => x.EnfileiradaEm).SetElementName("enfileiradaEm");
            cm.MapProperty(x => x.LiberadaEm).SetElementName("liberadaEm").SetIgnoreIfNull(true);

            cm.MapField("_servicos").SetElementName("servicos").SetIsRequired(true);
            cm.MapField("_pecas").SetElementName("pecas").SetIsRequired(true);
            cm.MapField("_etapas").SetElementName("etapas").SetIsRequired(true);
            cm.MapField("_apontamentos").SetElementName("apontamentos").SetIsRequired(true);

            cm.SetCreator(CriarSemConstrutor<OrdemDeProducao>);
        });
    }

    private static void RegistrarCancelamentoPrevio()
    {
        BsonClassMap.RegisterClassMap<CancelamentoPrevio>(cm =>
        {
            cm.SetDiscriminator(TipoCancelamentoPrevio);
            cm.SetDiscriminatorIsRequired(true);

            cm.MapProperty(x => x.Motivo).SetElementName("motivo");
            cm.MapProperty(x => x.RegistradoEm).SetElementName("registradoEm");

            cm.SetCreator(CriarSemConstrutor<CancelamentoPrevio>);
        });
    }

    private static object CriarSemConstrutor<T>() => RuntimeHelpers.GetUninitializedObject(typeof(T));

    private static ConstructorInfo ConstrutorUnico<T>() =>
        typeof(T).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single();
}
