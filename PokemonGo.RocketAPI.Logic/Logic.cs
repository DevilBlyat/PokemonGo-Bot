﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PokemonGo.RocketAPI.Enums;
using PokemonGo.RocketAPI.Extensions;
using PokemonGo.RocketAPI.GeneratedCode;
using PokemonGo.RocketAPI.Logic.Utils;
using PokemonGo.RocketAPI.Exceptions;
using System.Net;
using System.IO;
using System.Device.Location;
using PokemonGo.RocketAPI.Helpers;

namespace PokemonGo.RocketAPI.Logic
{

    public class Logic
    {
        public readonly Client _client;
        public readonly ISettings _clientSettings;
        public readonly Inventory _inventory;
        public TelegramUtil _telegram;
        public BotStats _botStats;
        private readonly Navigation _navigation;


        public Logic(ISettings clientSettings)
        {
            _clientSettings = clientSettings;
            _client = new Client(_clientSettings);
            _inventory = new Inventory(_client);
            _botStats = new BotStats();
        }

        public async Task Execute()
        {
            Logger.ColoredConsoleWrite(ConsoleColor.Green, $"Starting Execute on login server: {_clientSettings.AuthType}", LogLevel.Info);

            while (true)
            {
                try
                {
                    if (_clientSettings.AuthType == AuthType.Ptc)
                        await _client.DoPtcLogin(_clientSettings.PtcUsername, _clientSettings.PtcPassword);
                    else if (_clientSettings.AuthType == AuthType.Google)
                        await _client.DoGoogleLogin();

                    if (_clientSettings.TelegramAPIToken != "YourAccessToken" && _clientSettings.TelegramName != "YourTelegramNickname")
                    {
                        try
                        {
                            _telegram = new TelegramUtil(_client, new Telegram.Bot.TelegramBotClient(_clientSettings.TelegramAPIToken), _clientSettings, _inventory);

                            Logger.ColoredConsoleWrite(ConsoleColor.Green, "To Activate Informations with Telegram, write the Bot a message for more Informations");
                            var me = await _telegram.getClient().GetMeAsync();
                            _telegram.getClient().OnCallbackQuery += _telegram.BotOnCallbackQueryReceived;
                            _telegram.getClient().OnMessage += _telegram.BotOnMessageReceived;
                            _telegram.getClient().OnMessageEdited += _telegram.BotOnMessageReceived;
                            Logger.ColoredConsoleWrite(ConsoleColor.Green, "Telegram Name: " + me.Username);
                            _telegram.getClient().StartReceiving();
                        } catch (Exception)
                        {

                        }
                    }

                    await PostLoginExecute();
                }
                catch (PtcOfflineException)
                {
                    Logger.Error("PTC Server Offline. Trying to Restart in 20 Seconds...");
                    try
                    {
                        _telegram.getClient().StopReceiving();
                    }
                    catch (Exception)
                    {

                    }
                    await Task.Delay(10000);
                }
                catch (AccessTokenExpiredException)
                {
                    Logger.Error("PTC Server Offline, or Access Token expired. Restarting.");
                    try
                    {
                        _telegram.getClient().StopReceiving();
                    }
                    catch (Exception)
                    {

                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error: " + ex.Source);
                    Logger.Error($"{ex}");
                    Logger.ColoredConsoleWrite(ConsoleColor.Green, "Trying to Restart."); 
                    try
                    {
                        _telegram.getClient().StopReceiving();
                    } catch(Exception)
                    {

                    }
                }

                Logger.ColoredConsoleWrite(ConsoleColor.Red, "Restarting in 10 Seconds.");
                await Task.Delay(10000);
            }
        }

        public async Task PostLoginExecute()
        {

            while (true)
            {
                try
                {

                    await _client.SetServer();
                    await StatsLog(_client);
                    if (_clientSettings.EvolvePokemonsIfEnoughCandy)
                    {
                        await EvolveAllPokemonWithEnoughCandy();
                    }
                    await TransferDuplicatePokemon(true);
                    await RecycleItems();
                    await ExecuteFarmingPokestopsAndPokemons(_client);

                }
                catch (AccessTokenExpiredException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.Write($"Exception: {ex}", LogLevel.Error);
                }
                Logger.ColoredConsoleWrite(ConsoleColor.Green, "Starting again.. But waiting 10 Seconds..");
                await Task.Delay(10000);
            }
        }

        public async Task RepeatAction(int repeat, Func<Task> action)
        {
            for (int i = 0; i < repeat; i++)
                await action();
        }

        private async Task StatsLog(Client client)
        {
            var inventory = await client.GetInventory();
            var profil = await client.GetProfile();
            var stats = inventory.InventoryDelta.InventoryItems.Select(i => i.InventoryItemData.PlayerStats).ToArray();
            foreach (var c in stats)
            {
                if (c != null)
                {
                    int l = c.Level;

                    var expneeded = ((c.NextLevelXp - c.PrevLevelXp) - StringUtils.getExpDiff(c.Level));
                    var curexp = ((c.Experience - c.PrevLevelXp) - StringUtils.getExpDiff(c.Level));
                    var curexppercent = (Convert.ToDouble(curexp) / Convert.ToDouble(expneeded)) * 100;
                    var pokemonToEvolve = (await _inventory.GetPokemonToEvolve(null)).Count();

                    Logger.ColoredConsoleWrite(ConsoleColor.Cyan, "_____________________________");
                    Logger.ColoredConsoleWrite(ConsoleColor.Cyan, "Level: " + c.Level);
                    Logger.ColoredConsoleWrite(ConsoleColor.Cyan, "EXP Needed: " + expneeded);
                    Logger.ColoredConsoleWrite(ConsoleColor.Cyan, $"Current EXP: {curexp} ({Math.Round(curexppercent)}%)");
                    Logger.ColoredConsoleWrite(ConsoleColor.Cyan, "EXP to Level up: " + ((c.NextLevelXp) - (c.Experience)));
                    Logger.ColoredConsoleWrite(ConsoleColor.Cyan, "KM Walked: " + c.KmWalked);
                    Logger.ColoredConsoleWrite(ConsoleColor.Cyan, "PokeStops visited: " + c.PokeStopVisits);
                    Logger.ColoredConsoleWrite(ConsoleColor.Cyan, "Stardust: " + profil.Profile.Currency.ToArray()[1].Amount);
                    Logger.ColoredConsoleWrite(ConsoleColor.Cyan, "Pokemon to evolve: " + pokemonToEvolve);

                    Logger.ColoredConsoleWrite(ConsoleColor.Cyan, "_____________________________");

                    System.Console.Title = profil.Profile.Username + " Level " + c.Level + " - (" + ((c.Experience - c.PrevLevelXp) - 
                        StringUtils.getExpDiff(c.Level)) + " / " + ((c.NextLevelXp - c.PrevLevelXp) - StringUtils.getExpDiff(c.Level)) + " | " + Math.Round(curexppercent) + "%)   | Stardust: " + profil.Profile.Currency.ToArray()[1].Amount + " | " + _botStats.ToString();
                     
                }
            } 
        }


        private int count = 0;

        private async Task ExecuteFarmingPokestopsAndPokemons(Client client)
        {

            var distanceFromStart = LocationUtils.CalculateDistanceInMeters(_clientSettings.DefaultLatitude, _clientSettings.DefaultLongitude, _client.CurrentLat, _client.CurrentLng);

            if (_clientSettings.MaxWalkingRadiusInMeters != 0 && distanceFromStart > _clientSettings.MaxWalkingRadiusInMeters)
            {
                Logger.ColoredConsoleWrite(ConsoleColor.Green, "Youre outside of the defined Max Walking Radius. Walking back!");
                var update = await HumanLikeWalking(new GeoCoordinate(_clientSettings.DefaultLatitude, _clientSettings.DefaultLongitude), _clientSettings.WalkingSpeedInKilometerPerHour, null);
                var start = await HumanLikeWalking(new GeoCoordinate(_clientSettings.DefaultLatitude, _clientSettings.DefaultLongitude), _clientSettings.WalkingSpeedInKilometerPerHour, ExecuteCatchAllNearbyPokemons);
            }

            Resources.OutPutWalking = true;
            var mapObjects = await client.GetMapObjects();
            
            //var pokeStops = mapObjects.MapCells.SelectMany(i => i.Forts).Where(i => i.Type == FortType.Checkpoint && i.CooldownCompleteTimestampMs < DateTime.UtcNow.ToUnixTime());

            var pokeStops =
            pathByNearestNeighbour(
            mapObjects.MapCells.SelectMany(i => i.Forts)
            .Where(
                i =>
                i.Type == FortType.Checkpoint &&
                i.CooldownCompleteTimestampMs < DateTime.UtcNow.ToUnixTime())
                .OrderBy(
                i =>
                LocationUtils.CalculateDistanceInMeters(_client.CurrentLat, _client.CurrentLng, i.Latitude, i.Longitude)).ToArray(), _clientSettings.WalkingSpeedInKilometerPerHour);


            if (_clientSettings.MaxWalkingRadiusInMeters != 0)
            {
                pokeStops = pokeStops.Where(i => LocationUtils.CalculateDistanceInMeters(_client.CurrentLat, _client.CurrentLng, i.Latitude, i.Longitude) <= _clientSettings.MaxWalkingRadiusInMeters).ToArray();
                if (pokeStops.Count() == 0)
                {
                    Logger.ColoredConsoleWrite(ConsoleColor.Red, "We cant find any PokeStops in a range of " + _clientSettings.MaxWalkingRadiusInMeters + "m!");
                }
            }

           
            if (pokeStops.Count() == 0)
            {
                Logger.ColoredConsoleWrite(ConsoleColor.Red, "We cant find any PokeStops, which are unused! Probably Server unstable, or you visted them all. Retrying..");
                await ExecuteCatchAllNearbyPokemons();

            }
            else
            {
                Logger.ColoredConsoleWrite(ConsoleColor.Yellow, "We found " + pokeStops.Count() + " PokeStops near.");
            }

            foreach (var pokeStop in pokeStops)
            {

                await ExecuteCatchAllNearbyPokemons();
                await UseIncense();
                count++;
                if (count >= 3)
                {
                    count = 0;
                    await StatsLog(client);
                    if (_clientSettings.EvolvePokemonsIfEnoughCandy)
                    {
                        await EvolveAllPokemonWithEnoughCandy();
                    }
                    await TransferDuplicatePokemon(true);
                    await RecycleItems();
                }

                var distance = LocationUtils.CalculateDistanceInMeters(_client.CurrentLat, _client.CurrentLng, pokeStop.Latitude, pokeStop.Longitude);
                var fortInfo = await client.GetFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);
                Logger.ColoredConsoleWrite(ConsoleColor.Green, $"Next Pokestop: {fortInfo.Name} in {distance:0.##}m distance.");
                var update = await HumanLikeWalking(new GeoCoordinate(pokeStop.Latitude, pokeStop.Longitude), _clientSettings.WalkingSpeedInKilometerPerHour, ExecuteCatchAllNearbyPokemons);

                ////var fortInfo = await client.GetFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);
                var fortSearch = await client.SearchFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);

                if (fortSearch.ExperienceAwarded > 0)
                {
                    _botStats.addExperience(fortSearch.ExperienceAwarded);
                    Logger.ColoredConsoleWrite(ConsoleColor.Green, $"Farmed XP: {fortSearch.ExperienceAwarded}, Gems: { fortSearch.GemsAwarded}, Eggs: {fortSearch.PokemonDataEgg} Items: {StringUtils.GetSummedFriendlyNameOfItemAwardList(fortSearch.ItemsAwarded)}", LogLevel.Info);
                }

                await RandomHelper.RandomDelay(50, 200);
            }
            if (_clientSettings.WalkBackToDefaultLocation)
            {
                Logger.ColoredConsoleWrite(ConsoleColor.Green, "Walking back to Default Location.");
                await HumanLikeWalking(new GeoCoordinate(_clientSettings.DefaultLatitude, _clientSettings.DefaultLongitude), _clientSettings.WalkingSpeedInKilometerPerHour, ExecuteCatchAllNearbyPokemons);
            }
        }

        private async Task ExecuteCatchAllNearbyPokemons()
        {
            var client = _client;
            var mapObjects = await client.GetMapObjects();

            //var pokemons = mapObjects.MapCells.SelectMany(i => i.CatchablePokemons);
            var pokemons =
               mapObjects.MapCells.SelectMany(i => i.CatchablePokemons)
               .OrderBy(
                   i =>
                   LocationUtils.CalculateDistanceInMeters(_client.CurrentLat, _client.CurrentLng, i.Latitude, i.Longitude));

            if (pokemons != null && pokemons.Any())
                Logger.ColoredConsoleWrite(ConsoleColor.Magenta, $"Found {pokemons.Count()} catchable Pokemon(s).");

            foreach (var pokemon in pokemons)
            {
                if (_clientSettings.catchPokemonSkipList.Contains(pokemon.PokemonId))
                {
                    Logger.ColoredConsoleWrite(ConsoleColor.Green, "Skipped Pokemon: " + pokemon.PokemonId);
                    continue;
                }

                var distance = LocationUtils.CalculateDistanceInMeters(_client.CurrentLat, _client.CurrentLng, pokemon.Latitude, pokemon.Longitude);
                await Task.Delay(distance > 100 ? 1000 : 100);
                var encounterPokemonResponse = await _client.EncounterPokemon(pokemon.EncounterId, pokemon.SpawnpointId);
                
                if (encounterPokemonResponse.Status == EncounterResponse.Types.Status.EncounterSuccess)
                {
                    var bestPokeball = await GetBestBall(encounterPokemonResponse?.WildPokemon);
                    if (bestPokeball == MiscEnums.Item.ITEM_UNKNOWN)
                    {
                        Logger.ColoredConsoleWrite(ConsoleColor.Red, $"We dont own Pokeballs! - We missed a {pokemon.PokemonId} with CP {encounterPokemonResponse?.WildPokemon?.PokemonData?.Cp}");
                        return;
                    }
                    CatchPokemonResponse caughtPokemonResponse;
                    do
                    {
                        
                        var inventoryBerries = await _inventory.GetItems();
                        var probability = encounterPokemonResponse?.CaptureProbability?.CaptureProbability_?.FirstOrDefault();
                        var bestBerry = await GetBestBerry(encounterPokemonResponse?.WildPokemon);
                        var berries = inventoryBerries.Where(p => (ItemId)p.Item_ == bestBerry).FirstOrDefault();
                        if (bestBerry != ItemId.ItemUnknown && probability.HasValue && probability.Value < 0.35)
                        {
                            //Throw berry is we can
                            var useRaspberry = await _client.UseCaptureItem(pokemon.EncounterId, bestBerry, pokemon.SpawnpointId);
                            Logger.ColoredConsoleWrite(ConsoleColor.Green, $"Used {bestBerry}. Remaining: {berries.Count}.", LogLevel.Info);
                            await RandomHelper.RandomDelay(50, 200);
                        }

                        caughtPokemonResponse = await _client.CatchPokemon(pokemon.EncounterId, pokemon.SpawnpointId, pokemon.Latitude, pokemon.Longitude, bestPokeball);
                    }
                    while (caughtPokemonResponse.Status == CatchPokemonResponse.Types.CatchStatus.CatchMissed || caughtPokemonResponse.Status == CatchPokemonResponse.Types.CatchStatus.CatchEscape);

                    if (caughtPokemonResponse.Status == CatchPokemonResponse.Types.CatchStatus.CatchSuccess)
                    {
                        foreach (int xp in caughtPokemonResponse.Scores.Xp)
                            _botStats.addExperience(xp);

                        Logger.ColoredConsoleWrite(ConsoleColor.Magenta, $"We caught a {StringUtils.getPokemonNameByLanguage(_clientSettings, pokemon.PokemonId)} with CP {encounterPokemonResponse?.WildPokemon?.PokemonData?.Cp} ({PokemonInfo.CalculatePokemonPerfection(encounterPokemonResponse?.WildPokemon.PokemonData)}% perfect) using a {bestPokeball}");

                        //try
                        //{
                        //    var r = (HttpWebRequest)WebRequest.Create("http://pokemon.becher.xyz/index.php?pokeName=" + pokemon.PokemonId);
                        //    var rp = (HttpWebResponse)r.GetResponse();
                        //    var rps = new StreamReader(rp.GetResponseStream()).ReadToEnd();
                        //    Logger.ColoredConsoleWrite(ConsoleColor.Magenta, $"We caught a {pokemon.PokemonId} ({rps}) with CP {encounterPokemonResponse?.WildPokemon?.PokemonData?.Cp} using a {bestPokeball}");
                        //} catch (Exception)
                        //{
                        //    Logger.ColoredConsoleWrite(ConsoleColor.Magenta, $"We caught a {pokemon.PokemonId} (Language Server Offline) with CP {encounterPokemonResponse?.WildPokemon?.PokemonData?.Cp} using a {bestPokeball}");
                        //}
                       
                        _botStats.addPokemon(1);
                    }
                    else
                    {
                        Logger.ColoredConsoleWrite(ConsoleColor.DarkYellow, $"{StringUtils.getPokemonNameByLanguage(_clientSettings, pokemon.PokemonId)} with CP {encounterPokemonResponse?.WildPokemon?.PokemonData?.Cp} ({PokemonInfo.CalculatePokemonPerfection(encounterPokemonResponse?.WildPokemon.PokemonData)} % perfect) got away while using a {bestPokeball}..");
                    }
                }
                else
                {
                    Logger.ColoredConsoleWrite(ConsoleColor.Red, $"Error Catching Pokemon: {encounterPokemonResponse?.Status}");
                }
                await RandomHelper.RandomDelay(50, 200);
            }
        }

        private async Task EvolveAllPokemonWithEnoughCandy(IEnumerable<PokemonId> filter = null)
        {
            var pokemonToEvolve = await _inventory.GetPokemonToEvolve(filter);
            if (pokemonToEvolve.Count() != 0)
            {
                if(_clientSettings.UseLuckyEgg)
                {
                    await UseLuckyEgg(_client);
                }
            }
            foreach (var pokemon in pokemonToEvolve)
            {

                if (!_clientSettings.pokemonsToEvolve.Contains(pokemon.PokemonId))
                {
                    continue;
                }

                count++;
                if (count == 6)
                {
                    count = 0;
                    await StatsLog(_client);
                }
                

                var evolvePokemonOutProto = await _client.EvolvePokemon((ulong)pokemon.Id);

                if (evolvePokemonOutProto.Result == EvolvePokemonOut.Types.EvolvePokemonStatus.PokemonEvolvedSuccess)
                {
                    Logger.ColoredConsoleWrite(ConsoleColor.Green, $"Evolved {StringUtils.getPokemonNameByLanguage(_clientSettings,pokemon.PokemonId)} with {pokemon.Cp} CP ({PokemonInfo.CalculatePokemonPerfection(pokemon)} % perfect) successfully to {StringUtils.getPokemonNameByLanguage(_clientSettings, evolvePokemonOutProto.EvolvedPokemon.PokemonType)} with {evolvePokemonOutProto.EvolvedPokemon.Cp} CP ({PokemonInfo.CalculatePokemonPerfection(evolvePokemonOutProto.EvolvedPokemon)} % perfect) for {evolvePokemonOutProto.ExpAwarded}xp", LogLevel.Info);
                    _botStats.addExperience(evolvePokemonOutProto.ExpAwarded);
                }
                else
                {
                    Logger.ColoredConsoleWrite(ConsoleColor.Red, $"Failed to evolve {pokemon.PokemonId}. EvolvePokemonOutProto.Result was {evolvePokemonOutProto.Result}, stopping evolving {pokemon.PokemonId}", LogLevel.Info);
                }

                await RandomHelper.RandomDelay(1000, 2000);
            }
        }

        private async Task TransferDuplicatePokemon(bool keepPokemonsThatCanEvolve = false)
        {
            if (_clientSettings.TransferDoublePokemons)
            {
                var duplicatePokemons = await _inventory.GetDuplicatePokemonToTransfer(keepPokemonsThatCanEvolve);

                foreach (var duplicatePokemon in duplicatePokemons)
                {
                    if (!_clientSettings.pokemonsToHold.Contains(duplicatePokemon.PokemonId))
                    {

                        if (duplicatePokemon.Cp > _clientSettings.DontTransferWithCPOver)
                        {
                            continue;
                        }

                        var bestPokemonOfType = await _inventory.GetHighestCPofType(duplicatePokemon);

                        var transfer = await _client.TransferPokemon(duplicatePokemon.Id);
                        Logger.ColoredConsoleWrite(ConsoleColor.Yellow, $"Transfer {StringUtils.getPokemonNameByLanguage(_clientSettings, duplicatePokemon.PokemonId)} with {duplicatePokemon.Cp} CP ({PokemonInfo.CalculatePokemonPerfection(duplicatePokemon)} % perfect) (Best: {bestPokemonOfType} CP)", LogLevel.Info);
                        await RandomHelper.RandomDelay(500, 700);
                    }
                }
            }
        }

        private async Task RecycleItems()
        {
            var items = await _inventory.GetItemsToRecycle(_clientSettings);

            foreach (var item in items)
            {
                var transfer = await _client.RecycleItem((ItemId)item.Item_, item.Count);
                Logger.ColoredConsoleWrite(ConsoleColor.Yellow, $"Recycled {item.Count}x {(ItemId)item.Item_}", LogLevel.Info);
                await RandomHelper.RandomDelay(500, 700);
            }
        }

        private async Task<MiscEnums.Item> GetBestBall(WildPokemon pokemon)
        {
            var pokemonCp = pokemon?.PokemonData?.Cp;

            var items = await _inventory.GetItems();
            var balls = items.Where(i => ((MiscEnums.Item)i.Item_ == MiscEnums.Item.ITEM_POKE_BALL
                                      || (MiscEnums.Item)i.Item_ == MiscEnums.Item.ITEM_GREAT_BALL
                                      || (MiscEnums.Item)i.Item_ == MiscEnums.Item.ITEM_ULTRA_BALL
                                      || (MiscEnums.Item)i.Item_ == MiscEnums.Item.ITEM_MASTER_BALL) && i.Count > 0).GroupBy(i => ((MiscEnums.Item)i.Item_)).ToList();
            if (balls.Count == 0) return MiscEnums.Item.ITEM_UNKNOWN;

            var pokeBalls = balls.Any(g => g.Key == MiscEnums.Item.ITEM_POKE_BALL);
            var greatBalls = balls.Any(g => g.Key == MiscEnums.Item.ITEM_GREAT_BALL);
            var ultraBalls = balls.Any(g => g.Key == MiscEnums.Item.ITEM_ULTRA_BALL);
            var masterBalls = balls.Any(g => g.Key == MiscEnums.Item.ITEM_MASTER_BALL);

            if (masterBalls && pokemonCp >= 2000)
                return MiscEnums.Item.ITEM_MASTER_BALL;
            else if (ultraBalls && pokemonCp >= 2000)
                return MiscEnums.Item.ITEM_ULTRA_BALL;
            else if (greatBalls && pokemonCp >= 2000)
                return MiscEnums.Item.ITEM_GREAT_BALL;

            if (ultraBalls && pokemonCp >= 1000)
                return MiscEnums.Item.ITEM_ULTRA_BALL;
            else if (greatBalls && pokemonCp >= 1000)
                return MiscEnums.Item.ITEM_GREAT_BALL;

            if (greatBalls && pokemonCp >= 500)
                return MiscEnums.Item.ITEM_GREAT_BALL;

            return balls.OrderBy(g => g.Key).First().Key;
        }

        private async Task<ItemId> GetBestBerry(WildPokemon pokemon)
        {
            var pokemonCp = pokemon?.PokemonData?.Cp;

            var items = await _inventory.GetItems();
            var berries = items.Where(i => (ItemId)i.Item_ == ItemId.ItemRazzBerry
                                        || (ItemId)i.Item_ == ItemId.ItemBlukBerry
                                        || (ItemId)i.Item_ == ItemId.ItemNanabBerry
                                        || (ItemId)i.Item_ == ItemId.ItemWeparBerry
                                        || (ItemId)i.Item_ == ItemId.ItemPinapBerry).GroupBy(i => ((ItemId)i.Item_)).ToList();
            if (berries.Count == 0 || pokemonCp <= 350) return ItemId.ItemUnknown;

            var razzBerryCount = await _inventory.GetItemAmountByType(MiscEnums.Item.ITEM_RAZZ_BERRY);
            var blukBerryCount = await _inventory.GetItemAmountByType(MiscEnums.Item.ITEM_BLUK_BERRY);
            var nanabBerryCount = await _inventory.GetItemAmountByType(MiscEnums.Item.ITEM_NANAB_BERRY);
            var weparBerryCount = await _inventory.GetItemAmountByType(MiscEnums.Item.ITEM_WEPAR_BERRY);
            var pinapBerryCount = await _inventory.GetItemAmountByType(MiscEnums.Item.ITEM_PINAP_BERRY);

            if (pinapBerryCount > 0 && pokemonCp >= 2000)
                return ItemId.ItemPinapBerry;
            else if (weparBerryCount > 0 && pokemonCp >= 2000)
                return ItemId.ItemWeparBerry;
            else if (nanabBerryCount > 0 && pokemonCp >= 2000)
                return ItemId.ItemNanabBerry;
            else if (nanabBerryCount > 0 && pokemonCp >= 2000)
                return ItemId.ItemBlukBerry;

            if (weparBerryCount > 0 && pokemonCp >= 1500)
                return ItemId.ItemWeparBerry;
            else if (nanabBerryCount > 0 && pokemonCp >= 1500)
                return ItemId.ItemNanabBerry;
            else if (blukBerryCount > 0 && pokemonCp >= 1500)
                return ItemId.ItemBlukBerry;

            if (nanabBerryCount > 0 && pokemonCp >= 1000)
                return ItemId.ItemNanabBerry;
            else if (blukBerryCount > 0 && pokemonCp >= 1000)
                return ItemId.ItemBlukBerry;

            if (blukBerryCount > 0 && pokemonCp >= 500)
                return ItemId.ItemBlukBerry;

            return berries.OrderBy(g => g.Key).First().Key;
        }

        DateTime lastegguse;

        public async Task UseLuckyEgg(Client client)
        {
            if (lastegguse == null)
            {
                lastegguse = DateTime.Now;
            }

            var inventory = await _inventory.GetItems();
            var luckyEggs = inventory.Where(p => (ItemId)p.Item_ == ItemId.ItemLuckyEgg);
            var luckyEgg = luckyEggs.FirstOrDefault();

            if (lastegguse > DateTime.Now.AddSeconds(5))
            {
                return;
            }

            if (luckyEgg == null || luckyEgg.Count <= 0)
                return;

            await _client.UseItemXpBoost(ItemId.ItemLuckyEgg);
            Logger.ColoredConsoleWrite(ConsoleColor.Cyan, $"Used Lucky Egg, remaining: {luckyEgg.Count - 1}");
            lastegguse = DateTime.Now.AddMinutes(30);
            await Task.Delay(3000);
        }

        DateTime lastincenseuse;

        public async Task UseIncense()
        {
            if (lastincenseuse == null)
            {
                lastincenseuse = DateTime.Now;
            }

            var inventory = await _inventory.GetItems();
            var incsense = inventory.Where(p => (ItemId)p.Item_ == ItemId.ItemIncenseOrdinary).FirstOrDefault();

            if (lastincenseuse > DateTime.Now.AddSeconds(5))
            {
                TimeSpan duration = lastincenseuse - DateTime.Now;
                Logger.ColoredConsoleWrite(ConsoleColor.Cyan, $"Incense still running: {duration.Minutes}m{duration.Seconds}s");
                return;
            }
            if (incsense == null || incsense.Count <= 0)
            {
                return;
            }

            await _client.UseItemIncense(ItemId.ItemIncenseOrdinary);
            Logger.ColoredConsoleWrite(ConsoleColor.Cyan, $"Used Incsense, remaining: {incsense.Count - 1}");
            lastincenseuse = DateTime.Now.AddMinutes(30);
            await Task.Delay(3000);
        }

        private double _distance(double Lat1, double Lng1, double Lat2, double Lng2)
        {
            double r_earth = 6378137;
            double d_lat = (Lat2 - Lat1) * Math.PI / 180;
            double d_lon = (Lng2 - Lng1) * Math.PI / 180;
            double alpha = Math.Sin(d_lat / 2) * Math.Sin(d_lat / 2)
                + Math.Cos(Lat1 * Math.PI / 180) * Math.Cos(Lat2 * Math.PI / 180)
                * Math.Sin(d_lon / 2) * Math.Sin(d_lon / 2);
            double d = 2 * r_earth * Math.Atan2(Math.Sqrt(alpha), Math.Sqrt(1 - alpha));
            return d;
        }

        public static double DistanceBetween2Coordinates(double Lat1, double Lng1, double Lat2, double Lng2)
        {
            double r_earth = 6378137;
            double d_lat = (Lat2 - Lat1) * Math.PI / 180;
            double d_lon = (Lng2 - Lng1) * Math.PI / 180;
            double alpha = Math.Sin(d_lat / 2) * Math.Sin(d_lat / 2)
                + Math.Cos(Lat1 * Math.PI / 180) * Math.Cos(Lat2 * Math.PI / 180)
                * Math.Sin(d_lon / 2) * Math.Sin(d_lon / 2);
            double d = 2 * r_earth * Math.Atan2(Math.Sqrt(alpha), Math.Sqrt(1 - alpha));
            return d;
        }
        private const double SpeedDownTo = 10 / 3.6;

        public async Task<PlayerUpdateResponse> HumanLikeWalking(GeoCoordinate targetLocation,
            double walkingSpeedInKilometersPerHour, Func<Task> functionExecutedWhileWalking)
        {
            var speedInMetersPerSecond = walkingSpeedInKilometersPerHour / 3.6;

            var sourceLocation = new GeoCoordinate(_client.CurrentLat, _client.CurrentLng);
            var distanceToTarget = LocationUtils.CalculateDistanceInMeters(sourceLocation, targetLocation);
            Logger.ColoredConsoleWrite(ConsoleColor.DarkCyan, $"Distance to target location: {distanceToTarget:0.##} meters. Will take {distanceToTarget / speedInMetersPerSecond:0.##} seconds!");

            var nextWaypointBearing = LocationUtils.DegreeBearing(sourceLocation, targetLocation);
            var nextWaypointDistance = speedInMetersPerSecond;
            var waypoint = LocationUtils.CreateWaypoint(sourceLocation, nextWaypointDistance, nextWaypointBearing);

            //Initial walking
            var requestSendDateTime = DateTime.Now;
            var result =
                await
                    _client.UpdatePlayerLocation(waypoint.Latitude, waypoint.Longitude, _client.getSettingHandle().DefaultAltitude);

            if (functionExecutedWhileWalking != null)
                await functionExecutedWhileWalking();
            do
            {
                var millisecondsUntilGetUpdatePlayerLocationResponse =
                    (DateTime.Now - requestSendDateTime).TotalMilliseconds;

                sourceLocation = new GeoCoordinate(_client.CurrentLat, _client.CurrentLng);
                var currentDistanceToTarget = LocationUtils.CalculateDistanceInMeters(sourceLocation, targetLocation);

                if (currentDistanceToTarget < 40)
                {
                    if (speedInMetersPerSecond > SpeedDownTo)
                    {
                        Logger.ColoredConsoleWrite(ConsoleColor.DarkCyan, $"We are within 40 meters of the target. Speeding down to 10 km/h to not pass the target.");
                        speedInMetersPerSecond = SpeedDownTo;
                    }
                }

                nextWaypointDistance = Math.Min(currentDistanceToTarget,
                    millisecondsUntilGetUpdatePlayerLocationResponse / 1000 * speedInMetersPerSecond);
                nextWaypointBearing = LocationUtils.DegreeBearing(sourceLocation, targetLocation);
                waypoint = LocationUtils.CreateWaypoint(sourceLocation, nextWaypointDistance, nextWaypointBearing);

                requestSendDateTime = DateTime.Now;
                result =
                    await
                        _client.UpdatePlayerLocation(waypoint.Latitude, waypoint.Longitude,
                            _client.getSettingHandle().DefaultAltitude);
                await Task.Delay(Math.Min((int)(distanceToTarget / speedInMetersPerSecond * 1000), 3000));
                await ExecuteCatchAllNearbyPokemons();
            } while (LocationUtils.CalculateDistanceInMeters(sourceLocation, targetLocation) >= 30);

            return result;
        }

        private double calcTime(ref FortData[] pokeStops, List<int> _chromosome, double walkingSpeedInKilometersPerHour)
        {
            double time = 0.0;
            for (int i = 0; i < _chromosome.Count - 1; ++i)
            {
                double distance = DistanceBetween2Coordinates(pokeStops[_chromosome[i]].Latitude, pokeStops[_chromosome[i]].Longitude, pokeStops[_chromosome[i + 1]].Latitude, pokeStops[_chromosome[i + 1]].Longitude);
                if (distance <= 40)
                {
                    time += distance / SpeedDownTo;
                }
                else
                {
                    time += distance * 3.6 / walkingSpeedInKilometersPerHour;
                }
            }
            return time;
        }
        private int calcFitness(ref FortData[] pokeStops, List<int> _chromosome, double walkingSpeedInKilometersPerHour)
        {
            if (_chromosome.Count <= 2) return 0;

            double time = 0.0;
            double length = 0.0;
            for (int i = 0; i < _chromosome.Count - 1; ++i)
            {
                double distance = DistanceBetween2Coordinates(pokeStops[_chromosome[i]].Latitude, pokeStops[_chromosome[i]].Longitude, pokeStops[_chromosome[i + 1]].Latitude, pokeStops[_chromosome[i + 1]].Longitude);
                if (distance <= 40)
                {
                    time += distance / SpeedDownTo;
                }
                else
                {
                    time += distance * 3.6 / walkingSpeedInKilometersPerHour;
                }
                length += distance;
            }

            if (time <= 380 || !(time > 0.0)) return 0;

            if (_client.getSettingHandle().navigation_option == 1)
            {
                return Convert.ToInt32((_chromosome.Count * 10000) / time);
            }
            else
            {
                return Convert.ToInt32(_chromosome.Count * length / time);
            }
        }
        private List<int> calcCrossing(List<int> _chromosome1, List<int> _chromosome2)
        {
            List<int> child = new List<int>(_chromosome1);

            if (child.Count <= 3) return child;

            Random rnd = new Random();
            int p = rnd.Next(1, child.Count - 2);
            for (; p < child.Count - 1; ++p)
            {
                for (int i = 0; i < _chromosome2.Count; ++i)
                {
                    var tempIndex = child.FindIndex(x => x == _chromosome2[i]);
                    if (tempIndex > p || tempIndex < 0)
                    {
                        child[p] = _chromosome2[i];
                        break;
                    }
                }
            }

            return child;
        }
        private void mutate(ref List<int> _chromosome)
        {
            Random rnd = new Random();
            int i1 = rnd.Next(1, _chromosome.Count - 2), i2 = rnd.Next(1, _chromosome.Count - 2);
            int temp = _chromosome[i1];
            _chromosome[i1] = _chromosome[i2];
            _chromosome[i2] = temp;
        }

        private List<List<int>> selection(FortData[] pokeStops, List<List<int>> population, double walkingSpeedInKilometersPerHour)
        {
            List<List<int>> listSelection = new List<List<int>>();
            int sumPop = 0;
            List<int> fittnes = new List<int>();

            for (int c = 0; c < population.Count; ++c)
            {
                var temp = calcFitness(ref pokeStops, population[c], walkingSpeedInKilometersPerHour);
                sumPop += temp;
                fittnes.Add(temp);
            }
            List<int> fittnesSorted = new List<int>(fittnes);
            fittnesSorted.Sort();

            if (sumPop < 2) return listSelection;

            Random rnd = new Random();
            int selcetedChr = -1;
            do
            {
                var tempIndex = rnd.Next(0, sumPop);
                int tempSumPop = 0;
                for (int c = fittnesSorted.Count - 1; c > 0; --c)
                {
                    tempSumPop += fittnesSorted[c];
                    if (tempSumPop > tempIndex)
                    {
                        var tempSelcetedChr = fittnes.FindIndex(x => x == fittnesSorted[c]);
                        if (tempSelcetedChr != selcetedChr && !(tempSelcetedChr < 0))
                        {
                            selcetedChr = tempSelcetedChr;
                            listSelection.Add(population[selcetedChr]);
                            break;
                        }
                    }

                }
            } while (listSelection.Count < 2);



            return listSelection;
        }
        public FortData[] pathByNearestNeighbour(FortData[] pokeStops, double walkingSpeedInKilometersPerHour)
        {
            ////Start Gen. alg.
            //if (pokeStops.Length > 15)
            //{
            //    //Config
            //    int ITERATIONS = 100000;
            //    int POPSIZE = pokeStops.Length * 60;
            //    double CROSSPROP = 99;
            //    double MUTPROP = 20;


            //    List<List<int>> population = new List<List<int>>();
            //    Random rnd = new Random();
            //    //Create Population
            //    for (var i = POPSIZE; i > 0; --i)
            //    {
            //        List<int> tempChromosome = new List<int>();
            //        int items = rnd.Next(2, pokeStops.Length * 3 / 4);
            //        do
            //        {
            //            int tempIndex = rnd.Next(0, pokeStops.Length - 1);
            //            //Add only if new Index
            //            while (tempChromosome.Exists(x => x == tempIndex))
            //            {
            //                tempIndex = rnd.Next(0, pokeStops.Length - 1);
            //            }
            //            tempChromosome.Add(tempIndex);
            //        } while (--items > 0);

            //        if (calcFitness(ref pokeStops, tempChromosome, walkingSpeedInKilometersPerHour) > 0.0)
            //        {
            //            tempChromosome.Add(tempChromosome[0]);
            //            population.Add(tempChromosome);
            //        }
            //    }

            //    if (population.Count > 10)
            //    {
            //        for (int i = 0; i < ITERATIONS; ++i)
            //        {
            //            //Selection
            //            var parents = selection(pokeStops, population, walkingSpeedInKilometersPerHour);
            //            List<int> child1 = parents[0], child2 = parents[1];
            //            //Crossing
            //            if (rnd.Next(0, 100) < CROSSPROP)
            //            {
            //                child1 = calcCrossing(parents[0], parents[1]);
            //                child2 = calcCrossing(parents[1], parents[0]);
            //            }
            //            //Mutation
            //            if (rnd.Next(0, 100) < MUTPROP)
            //            {
            //                mutate(ref child1);
            //            }
            //            if (rnd.Next(0, 100) < MUTPROP)
            //            {
            //                mutate(ref child2);
            //            }

            //            //Replace
            //            List<int> fittnes = new List<int>();
            //            int sumPop = 0;
            //            for (int c = 0; c < population.Count; ++c)
            //            {
            //                var temp = calcFitness(ref pokeStops, population[c], walkingSpeedInKilometersPerHour);
            //                sumPop += temp;
            //                fittnes.Add(temp);
            //            }
            //            List<int> fittnesSorted = new List<int>(fittnes);
            //            fittnesSorted.Sort();

            //            if (fittnesSorted[0] <= calcFitness(ref pokeStops, child1, walkingSpeedInKilometersPerHour))
            //            {
            //                var tempSelcetedChr = fittnes.FindIndex(x => x == fittnesSorted[0]);
            //                population[tempSelcetedChr] = child1;
            //            }
            //            if (fittnesSorted[1] <= calcFitness(ref pokeStops, child2, walkingSpeedInKilometersPerHour))
            //            {
            //                var tempSelcetedChr = fittnes.FindIndex(x => x == fittnesSorted[1]);
            //                population[tempSelcetedChr] = child2;
            //            }

            //            //get best Generation
            //            List<int> fittnes2 = new List<int>();
            //            for (int c = 0; c < population.Count; ++c)
            //            {
            //                var temp = calcFitness(ref pokeStops, population[c], walkingSpeedInKilometersPerHour);
            //                fittnes2.Add(temp);
            //            }
            //            List<int> fittnesSorted2 = new List<int>(fittnes2);
            //            fittnesSorted2.Sort();
            //            var tempSelcetedChr2 = fittnes2.FindIndex(x => x == fittnesSorted2[fittnesSorted2.Count - 1]);

            //            List<FortData> newPokeStops = new List<FortData>();
            //            foreach (var element in population[tempSelcetedChr2])
            //            {
            //                newPokeStops.Add(pokeStops[element]);
            //            }
            //            Logger.ColoredConsoleWrite(ConsoleColor.Yellow, $"{Math.Round(newPokeStops.Count * 3600 / calcTime(ref pokeStops, population[tempSelcetedChr2], walkingSpeedInKilometersPerHour))} PokeStops per Hour.");
            //            return newPokeStops.ToArray();
            //        }
            //    }
            //}
            //End gen. alg

            //Normal calculation
            for (var i = 1; i < pokeStops.Length - 1; i++)
            {
                var closest = i + 1;
                var cloestDist = LocationUtils.CalculateDistanceInMeters(pokeStops[i].Latitude, pokeStops[i].Longitude, pokeStops[closest].Latitude, pokeStops[closest].Longitude);
                for (var j = closest; j < pokeStops.Length; j++)
                {
                    var initialDist = cloestDist;
                    var newDist = LocationUtils.CalculateDistanceInMeters(pokeStops[i].Latitude, pokeStops[i].Longitude, pokeStops[j].Latitude, pokeStops[j].Longitude);
                    if (initialDist > newDist)
                    {
                        cloestDist = newDist;
                        closest = j;
                    }
                }
                var tmpPok = pokeStops[closest];
                pokeStops[closest] = pokeStops[i + 1];
                pokeStops[i + 1] = tmpPok;
            }
            return pokeStops;
        }
    }
}