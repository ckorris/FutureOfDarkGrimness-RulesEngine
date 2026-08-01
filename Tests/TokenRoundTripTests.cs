using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Round-trip tests for the per-unit and per-model token containers, exercising
    /// the same GameDataStore JSON path that the network layer uses to ship state
    /// between host and client. The goal of these tests is to guarantee that a
    /// networked player sees exactly the tokens the host attached to a unit/model —
    /// counts, owners, clear triggers, and payloads all preserved.
    /// </summary>
    [TestFixture]
    public class TokenRoundTripTests
    {
        // ----------------------------------------------------------------------
        // ModelData
        // ----------------------------------------------------------------------

        [Test]
        public void ModelTokens_SurviveGameDataStoreRoundTrip()
        {
            GameDataStore fromStore = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<float>(1)
                .RegisterType<Position>(1)
                .RegisterType<ModelData>(1)
                .RegisterType<Float2>(1)
                .Build();

            var model = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                initialPosition: new Position(),
                fromStore);

            // Attach a representative spread of tokens before serialising.
            var marker = new TokenType("RegenerativeStrengthMarker");
            model.Tokens.AddToken(MakeToken(marker, count: 3));

            DataReference modelRef = fromStore.Create(model);

            DataReference woundsRef   = model.RemainingWoundsBinding.Reference;
            DataReference positionRef = model.PositionBinding.Reference;
            DataReference facingRef = model.FacingBinding.Reference;
            string sWounds   = fromStore.GetValueAsJson<float>(woundsRef);
            string sPosition = fromStore.GetValueAsJson<Position>(positionRef);
            string sPositionFacing = fromStore.GetValueAsJson<Float2>(facingRef);
            string sModel    = fromStore.GetValueAsJson<ModelData>(modelRef);

            GameDataStore toStore = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<float>(1)
                .RegisterType<Position>(1)
                .RegisterType<ModelData>(1)
                .RegisterType<Float2>(1)
                .Build();

            toStore.CreateFromReferenceAndJson(woundsRef, sWounds);
            toStore.CreateFromReferenceAndJson(positionRef, sPosition);
            toStore.CreateFromReferenceAndJson(facingRef, sPositionFacing);
            toStore.CreateFromReferenceAndJson(modelRef, sModel);

            ModelData rehydrated = toStore.GetValue<ModelData>(modelRef);

            Assert.That(rehydrated.Tokens, Is.Not.Null, "rehydrated model must have a token container");
            Assert.That(rehydrated.Tokens.HasToken(marker), Is.True);
            Assert.That(rehydrated.Tokens.GetTokenCount(marker), Is.EqualTo(3));
        }

        // ----------------------------------------------------------------------
        // UnitData
        // ----------------------------------------------------------------------

        [Test]
        public void UnitTokens_SurviveGameDataStoreRoundTrip_PreservingTypeCountAndOwner()
        {
            GameDataStore fromStore = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<float>(1)
                .RegisterType<Position>(1)
                .RegisterType<ModelData>(1)
                .RegisterType<UnitData>(1)
                .RegisterType<Float2>(1)
                .Build();

            // Build a minimal model so the unit has something in its ModelBindings.
            var modelData = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                initialPosition: new Position(),
                fromStore);
            DataReference modelRef = fromStore.Create(modelData);
            DataBinding<ModelData> modelBinding = fromStore.GetDataBinding<ModelData>(modelRef);

            var unit = new UnitData(
                playerID: new PlayerID(Guid.NewGuid()),
                name: "RoundTripUnit",
                quality: 4,
                defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });

            // A representative spread: self-owned engine-known token (singleton-shape),
            // self-owned stack, and two cross-unit tokens with different owners (the case
            // that matters most for networked sync — different placers must stay distinct).
            var enemyA = new UnitID(Guid.NewGuid());
            var enemyB = new UnitID(Guid.NewGuid());
            var unstoppableMark = new TokenType("UnstoppableMark");

            unit.Tokens.AddToken(MakeToken(TokenType.Shaken, count: 1));
            unit.Tokens.AddToken(MakeToken(TokenType.SpellTokens, count: 4,
                clear: new TokenClearTrigger.RoundEnd()));
            unit.Tokens.AddToken(MakeToken(unstoppableMark, count: 1,
                owner: enemyA, clear: new TokenClearTrigger.OwnerDestroyed()));
            unit.Tokens.AddToken(MakeToken(unstoppableMark, count: 1,
                owner: enemyB, clear: new TokenClearTrigger.OwnerDestroyed()));

            DataReference unitRef = fromStore.Create(unit);

            // Serialise the whole chain a network message would carry.
            string sWounds   = fromStore.GetValueAsJson<float>(modelData.RemainingWoundsBinding.Reference);
            string sPosition = fromStore.GetValueAsJson<Position>(modelData.PositionBinding.Reference);
            string sPositionFacing = fromStore.GetValueAsJson<Float2>(modelData.FacingBinding.Reference);
            string sModel    = fromStore.GetValueAsJson<ModelData>(modelRef);
            string sUnit     = fromStore.GetValueAsJson<UnitData>(unitRef);

            // Rehydrate in a fresh store as a networked client would.
            GameDataStore toStore = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<float>(1)
                .RegisterType<Position>(1)
                .RegisterType<ModelData>(1)
                .RegisterType<UnitData>(1)
                .RegisterType<Float2>(1)
                .Build();

            toStore.CreateFromReferenceAndJson(modelData.RemainingWoundsBinding.Reference, sWounds);
            toStore.CreateFromReferenceAndJson(modelData.PositionBinding.Reference, sPosition);
            toStore.CreateFromReferenceAndJson(modelData.FacingBinding.Reference, sPositionFacing);
            toStore.CreateFromReferenceAndJson(modelRef, sModel);
            toStore.CreateFromReferenceAndJson(unitRef, sUnit);

            UnitData rehydrated = toStore.GetValue<UnitData>(unitRef);

            Assert.That(rehydrated.Tokens, Is.Not.Null);

            // Counts per type.
            Assert.That(rehydrated.Tokens.GetTokenCount(TokenType.Shaken), Is.EqualTo(1));
            Assert.That(rehydrated.Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(4));
            Assert.That(rehydrated.Tokens.GetTokenCount(unstoppableMark), Is.EqualTo(2),
                "two cross-unit Unstoppable Mark tokens (different owners) must both survive — " +
                "summed they should be 2");

            // Per-owner separation preserved.
            Assert.That(rehydrated.Tokens.TokensWithOwner(enemyA).Count(), Is.EqualTo(1));
            Assert.That(rehydrated.Tokens.TokensWithOwner(enemyB).Count(), Is.EqualTo(1));

            // Clear triggers preserved (discriminated-union round-trip).
            Token spellEntry = rehydrated.Tokens.GetAllTokens(TokenType.SpellTokens).Single();
            Assert.That(spellEntry.ClearTrigger, Is.InstanceOf<TokenClearTrigger.RoundEnd>());

            Token markFromA = rehydrated.Tokens.TokensWithOwner(enemyA).Single();
            Assert.That(markFromA.Type, Is.EqualTo(unstoppableMark));
            Assert.That(markFromA.ClearTrigger, Is.InstanceOf<TokenClearTrigger.OwnerDestroyed>());
        }

        [Test]
        public void UnitTokens_EmptyContainer_RoundTripsCleanly()
        {
            // A unit with no tokens must not deserialize to a null container.
            GameDataStore fromStore = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<float>(1)
                .RegisterType<Position>(1)
                .RegisterType<ModelData>(1)
                .RegisterType<UnitData>(1)
                .RegisterType<Float2>(1)
                .Build();

            var modelData = new ModelData(0.75f, new List<Weapon>(),
                new Position(), fromStore);
            DataReference modelRef = fromStore.Create(modelData);
            DataBinding<ModelData> modelBinding = fromStore.GetDataBinding<ModelData>(modelRef);

            var unit = new UnitData(
                playerID: new PlayerID(Guid.NewGuid()),
                name: "Empty",
                quality: 4,
                defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            DataReference unitRef = fromStore.Create(unit);

            string sWounds   = fromStore.GetValueAsJson<float>(modelData.RemainingWoundsBinding.Reference);
            string sPosition = fromStore.GetValueAsJson<Position>(modelData.PositionBinding.Reference);
            string sPositionFacing = fromStore.GetValueAsJson<Float2>(modelData.FacingBinding.Reference);
            string sModel    = fromStore.GetValueAsJson<ModelData>(modelRef);
            string sUnit     = fromStore.GetValueAsJson<UnitData>(unitRef);

            GameDataStore toStore = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<float>(1)
                .RegisterType<Position>(1)
                .RegisterType<ModelData>(1)
                .RegisterType<UnitData>(1)
                .RegisterType<Float2>(1)
                .Build();
            toStore.CreateFromReferenceAndJson(modelData.RemainingWoundsBinding.Reference, sWounds);
            toStore.CreateFromReferenceAndJson(modelData.PositionBinding.Reference, sPosition);
            toStore.CreateFromReferenceAndJson(modelData.FacingBinding.Reference, sPositionFacing);
            toStore.CreateFromReferenceAndJson(modelRef, sModel);
            toStore.CreateFromReferenceAndJson(unitRef, sUnit);

            UnitData rehydrated = toStore.GetValue<UnitData>(unitRef);

            Assert.That(rehydrated.Tokens, Is.Not.Null);
            Assert.That(rehydrated.Tokens.GetAllTokens().Count(), Is.Zero);
        }

        // ----------------------------------------------------------------------
        // Helper
        // ----------------------------------------------------------------------

        // #197 P12: the fractional marker over the same wire. A magnitude payload is the engine's only
        // non-integer token value, and it is the one thing about Regenerative Strength a networked client
        // or a reloaded save could silently round - a client seeing 1 marker where the host has 1.33 would
        // roll a different number of attack dice. SaveTypeRegistry pins the payload's stable $type id;
        // this pins that the VALUE survives with it.
        [Test]
        public void MagnitudeTokens_SurviveTheRoundTrip_WithTheirFraction()
        {
            GameDataStore fromStore = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<float>(1)
                .RegisterType<Position>(1)
                .RegisterType<ModelData>(1)
                .RegisterType<Float2>(1)
                .Build();

            var model = new ModelData(baseRadiusInches: 0.75f, weapons: new List<Weapon>(),
                initialPosition: new Position(), fromStore);

            var marker = TokenType.RegenerativeStrengthMarker;
            model.Tokens.AddToken(new Token(marker, 1, new TokenClearTrigger.ManualOnly(),
                Payload: new TokenPayload.Magnitude(1f / 3f)));

            DataReference modelRef = fromStore.Create(model);
            string sWounds = fromStore.GetValueAsJson<float>(model.RemainingWoundsBinding.Reference);
            string sPosition = fromStore.GetValueAsJson<Position>(model.PositionBinding.Reference);
            string sFacing = fromStore.GetValueAsJson<Float2>(model.FacingBinding.Reference);
            string sModel = fromStore.GetValueAsJson<ModelData>(modelRef);

            GameDataStore toStore = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<float>(1)
                .RegisterType<Position>(1)
                .RegisterType<ModelData>(1)
                .RegisterType<Float2>(1)
                .Build();

            toStore.CreateFromReferenceAndJson(model.RemainingWoundsBinding.Reference, sWounds);
            toStore.CreateFromReferenceAndJson(model.PositionBinding.Reference, sPosition);
            toStore.CreateFromReferenceAndJson(model.FacingBinding.Reference, sFacing);
            toStore.CreateFromReferenceAndJson(modelRef, sModel);

            ModelData rehydrated = toStore.GetValue<ModelData>(modelRef);

            Assert.That(rehydrated.Tokens.GetTokenMagnitude(marker), Is.EqualTo(1f / 3f).Within(0.0001f),
                "the fraction must cross the wire intact - rounding it here is the same bug as rounding " +
                "it at the ignore fold, just later");
            Assert.That(rehydrated.Tokens.GetAllTokens(marker).Single().Payload,
                Is.InstanceOf<TokenPayload.Magnitude>(), "and it is still a magnitude payload, not a count");
        }

        private static Token MakeToken(TokenType type, int count,
            UnitID? owner = null, TokenClearTrigger? clear = null) =>
            new Token(
                Type: type,
                Count: count,
                ClearTrigger: clear ?? new TokenClearTrigger.ManualOnly(),
                Payload: null,
                OwnerUnitID: owner,
                CreatedAtHook: null);
    }
}
