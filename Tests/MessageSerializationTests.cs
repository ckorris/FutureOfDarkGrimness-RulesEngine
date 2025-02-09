using FDG.Data;
using FDG.Data.Serialization;
using Newtonsoft.Json;
using NUnit.Framework;
using System;

namespace FDG.Tests
{
    [TestFixture]
    public class MessageSerializationTests
    {

        [Test]
        public void DataBindingDeserializeTest()
        {
            GameDataStore gameDataStoreFrom = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<int>(2)
                .Build();

            GameDataStore gameDataStoreTo = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<int>(2)
                .Build();

            //These should produce DataReferences that are identical.
            DataReference referenceFrom = gameDataStoreFrom.Create(5);
            DataReference referenceTo = gameDataStoreTo.Create(5);

            Assert.That(referenceFrom, Is.EqualTo(referenceTo));

            DataBinding<int> fromBinding = gameDataStoreFrom.GetDataBinding<int>(referenceFrom);
            DataBinding<int> toBinding = gameDataStoreTo.GetDataBinding<int>(referenceTo);

            var converterFrom = new DataBindingJsonConverter<int>(gameDataStoreFrom);
            var converterTo = new DataBindingJsonConverter<int>(gameDataStoreTo);

            string fromSerialized = JsonConvert.SerializeObject(fromBinding, [converterFrom]);

            DataBinding<int>? toDeserialized = JsonConvert.DeserializeObject<DataBinding<int>>(fromSerialized, [converterTo]);

            Assert.That(toDeserialized, Is.EqualTo(toBinding));
        }

        [Test]
        public void DataBindingFieldDeserializeTest()
        {
            GameDataStore gameDataStoreFrom = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<int>(2)
                .Build();

            GameDataStore gameDataStoreTo = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<int>(2)
                .Build();

            DataReference referenceFrom = gameDataStoreFrom.Create(5);
            DataReference referenceTo = gameDataStoreTo.Create(5);


            TestMessageWithIntBinding fromMessage = new TestMessageWithIntBinding(referenceFrom, gameDataStoreFrom);

            var converterFrom = new DataBindingJsonConverter<int>(gameDataStoreFrom);
            var converterTo = new DataBindingJsonConverter<int>(gameDataStoreTo);

            string fromSerialized = JsonConvert.SerializeObject(fromMessage, [converterFrom]);

            DataBinding<int> toBinding = gameDataStoreTo.GetDataBinding<int>(referenceTo);

            TestMessageWithIntBinding? toDeserialized = 
                JsonConvert.DeserializeObject<TestMessageWithIntBinding>(fromSerialized, [converterTo]);

            Assert.That(toDeserialized?.IntValueBinding, Is.EqualTo(toBinding));
        }

        [Test]
        public void DataBindingFieldDeserializeInGameDataStoreTest()
        {
            GameDataStore gameDataStoreFrom = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<int>(1)
                .RegisterType<TestMessageWithIntBinding>(1)
                .Build();

            GameDataStore gameDataStoreTo = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<int>(1)
                .RegisterType<TestMessageWithIntBinding>(1)
                .Build();

            DataReference intReferenceFrom = gameDataStoreFrom.Create(5);
            DataReference intReferenceTo = gameDataStoreTo.Create(5);

            DataBinding<int> toBinding = gameDataStoreTo.GetDataBinding<int>(intReferenceTo);

            TestMessageWithIntBinding fromMessage = new TestMessageWithIntBinding(intReferenceFrom, gameDataStoreFrom);
            DataReference messageReferenceFrom = gameDataStoreFrom.Create(fromMessage);

            string messageAsJson = gameDataStoreFrom.GetValueAsJson<TestMessageWithIntBinding>(messageReferenceFrom);

            Assert.That(gameDataStoreTo.IsValid(messageReferenceFrom, out _), Is.False);

            gameDataStoreTo.CreateFromReferenceAndJson(messageReferenceFrom, messageAsJson);

            Assert.That(gameDataStoreTo.IsValid(messageReferenceFrom, out _), Is.True);

            TestMessageWithIntBinding toMessage = gameDataStoreTo.GetValue<TestMessageWithIntBinding>(messageReferenceFrom);

            Assert.That(toMessage.IntValueBinding?.IsValid, Is.True);
            Assert.That(toMessage.IntValueBinding?.GetValue(), Is.EqualTo(5));
        }


        [Test]
        public void GameDataAwareAssignedBindingsTest()
        {
            int testValue = 7355608;

            List<Type> typeMap = new List<Type>() { typeof(int) };

            GameDataStore gameDataStore = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<int>(2)
                .Build();

            DataReference testReference = gameDataStore.Create(testValue);

            //Make a message the usual way. 
            TestMessageWithIntBinding testMessage = new TestMessageWithIntBinding(testReference, gameDataStore);


            Assert.That(testMessage.IntValueReference, Is.EqualTo(testReference));
            Assert.That(testMessage.IntValueBinding.IsValid, Is.True);
            Assert.That(testMessage.IntValueBinding.GetValue(), Is.EqualTo(testValue));

            string messageAsJson = JsonConvert.SerializeObject(testMessage);

            JsonSerializerSettings jsonsettings = new JsonSerializerSettings
            {
                ContractResolver = new DataBindingContractResolver(gameDataStore)
            };

            TestMessageWithIntBinding? deserializedTestMessage 
                = JsonConvert.DeserializeObject< TestMessageWithIntBinding>(messageAsJson, jsonsettings);

            Assert.That(deserializedTestMessage, Is.Not.Null);
            Assert.That(deserializedTestMessage.IntValueReference, Is.EqualTo(testReference));
            Assert.That(deserializedTestMessage.IntValueBinding.GetValue(), Is.EqualTo(testValue));
        }


        public class TestMessageWithIntBinding : IGameDataAware
        {
            public DataReference IntValueReference;

            public DataBinding<int>? IntValueBinding;

            [JsonConstructor]
            public TestMessageWithIntBinding(DataReference intValueReference)
            {
                IntValueReference = intValueReference;
            }

            public TestMessageWithIntBinding(DataReference intValueReference, 
                IReadWriteableGameDataStore gameDataStore)
            {
                IntValueReference = intValueReference;
                IntValueBinding = gameDataStore.GetDataBinding<int>(IntValueReference);
            }

            public void SetGameDataStore(IReadWriteableGameDataStore gameDataStore)
            {
                IntValueBinding = gameDataStore.GetDataBinding<int>(IntValueReference);
            }
        }

    }


}
