﻿using UnityEngine;

namespace WalkerSim
{
    internal class SpawnManager
    {
        static int _lastClassId = -1;

        static private bool CanSpawnZombie()
        {
            // Check for maximum count.
            var alive = GameStats.GetInt(EnumGameStats.EnemyCount);

            // We only allow half of the max count to be spawned to give sleepers some room.
            var maxAllowed = GamePrefs.GetInt(EnumGamePrefs.MaxSpawnedZombies) / 2;

            if (alive >= maxAllowed)
            {
                Logging.Debug("Max zombies reached, alive: {0}, max: {1}", alive, maxAllowed);
                return false;
            }

            return true;
        }

        static private bool CanSpawnAtPosition(UnityEngine.Vector3 position)
        {
            var world = GameManager.Instance.World;
            if (world == null)
            {
                return false;
            }

            if (!world.CanMobsSpawnAtPos(position))
            {
                return false;
            }

            if (world.isPositionInRangeOfBedrolls(position))
            {
                return false;
            }

            return true;
        }

        static private int GetEntityClassId(Chunk chunk, UnityEngine.Vector3 worldPos)
        {
            var world = GameManager.Instance.World;
            if (world == null)
            {
                return -1;
            }

            var biomeId = chunk.GetBiomeId(
                World.toBlockXZ(Mathf.FloorToInt(worldPos.x)),
                World.toBlockXZ(Mathf.FloorToInt(worldPos.z))
            );

            var biomeData = world.Biomes.GetBiome(biomeId);

            if (BiomeSpawningClass.list.TryGetValue(biomeData.m_sBiomeName, out BiomeSpawnEntityGroupList biomeList))
            {
                EDaytime eDaytime = world.IsDaytime() ? EDaytime.Day : EDaytime.Night;
                GameRandom gameRandom = world.GetGameRandom();

                var maxAttempts = System.Math.Min(biomeList.list.Count, 10);
                var lastPick = -1;
                for (int i = 0; i < maxAttempts; i++)
                {
                    var randomPick = gameRandom.RandomRange(0, biomeList.list.Count);
                    if (randomPick == lastPick)
                    {
                        i--;
                        continue;
                    }

                    lastPick = randomPick;
                    var group = biomeList.list[randomPick];

                    if (group.daytime != eDaytime)
                    {
                        continue;
                    }

                    if (!EntityGroups.IsEnemyGroup(group.entityGroupRefName))
                    {
                        continue;
                    }

                    if (!EntityGroups.list.ContainsKey(group.entityGroupRefName))
                    {
                        Logging.Err("Entity group not found: {0}", group.entityGroupRefName);
                        continue;
                    }

                    int lastClassId = _lastClassId;

                    int entityClassId = EntityGroups.GetRandomFromGroup(group.entityGroupRefName, ref lastClassId, gameRandom);
                    if (entityClassId != -1 && entityClassId != 0)
                    {
                        Logging.Debug("Selected entity class id {0} : {1}", entityClassId, group.entityGroupRefName);
                        return entityClassId;
                    }
                }
            }

            return -1;
        }

        static public int SpawnAgent(Agent agent)
        {
            var world = GameManager.Instance.World;
            if (world == null)
            {
                return -1;
            }

            if (!CanSpawnZombie())
            {
                return -1;
            }

            var worldPos = VectorUtils.ToUnity(agent.Position);

            // We leave y position to be adjusted by the terrain.
            worldPos.y = 0;

            var chunkPosX = World.toChunkXZ(Mathf.FloorToInt(worldPos.x));
            var chunkPosZ = World.toChunkXZ(Mathf.FloorToInt(worldPos.z));

            var chunk = world.GetChunkSync(chunkPosX, chunkPosZ) as Chunk;
            if (chunk == null)
            {
                Logging.DebugErr("Failed to spawn agent, chunk not loaded at {0}, {1}", chunkPosX, chunkPosZ);
                return -1;
            }

            var terrainHeight = world.GetTerrainHeight(Mathf.FloorToInt(worldPos.x), Mathf.FloorToInt(worldPos.z)) + 1;
            Logging.Debug("Terrain height at {0}, {1} is {2}", worldPos.x, worldPos.z, terrainHeight);

            // Adjust position height.
            worldPos.y = terrainHeight;

            if (!CanSpawnAtPosition(worldPos))
            {
                Logging.Debug("Failed to spawn agent, position not suitable at {0}, {1}, {2}", worldPos.x, worldPos.y, worldPos.z);
                return -1;
            }

            Logging.Out("Spawning agent at {0}, {1}, {2}", worldPos.x, worldPos.y, worldPos.z);

            // Use previously assigned entity class id.
            int entityClassId = agent.EntityClassId;
            if (entityClassId == -1 || entityClassId == 0)
            {
                entityClassId = GetEntityClassId(chunk, worldPos);
                if (entityClassId == -1 || entityClassId == 0)
                {
                    // Fallback to random.
                    int lastClassId = -1;
                    entityClassId = EntityGroups.GetRandomFromGroup("ZombiesAll", ref lastClassId);

                    if (entityClassId == -1 || entityClassId == 0)
                    {
                        Logging.Err("Failed to get entity class id from ZombiesAll.");
                        return -1;
                    }
                }
            }
            else
            {
                Logging.Debug("Using previous entity class id: {0}", entityClassId);
            }

            var rot = VectorUtils.ToUnity(agent.Velocity);
            rot.y = 0;
            rot.Normalize();

            var spawnedAgent = EntityFactory.CreateEntity(entityClassId, worldPos, rot) as EntityAlive;
            if (spawnedAgent == null)
            {
                Logging.DebugErr("Unable to create zombie entity!, Class Id: {0}, Pos: {1}", entityClassId, worldPos);
                return -1;
            }

            // Only update last class id if we successfully spawned the agent.
            _lastClassId = entityClassId;

            spawnedAgent.bIsChunkObserver = true;
            spawnedAgent.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);
            spawnedAgent.moveDirection = rot;

            if (spawnedAgent is EntityZombie spawnedZombie)
            {
                spawnedZombie.IsHordeZombie = true;
            }

            if (agent.Health != -1)
            {
                Logging.Debug("Using previous health: {0}", agent.Health);
                spawnedAgent.Health = agent.Health;
            }

            var destPos = worldPos + (rot * 150);
            spawnedAgent.SetInvestigatePosition(destPos, 6000, false);

            world.SpawnEntityInWorld(spawnedAgent);

            // Update the agent data.
            agent.EntityId = spawnedAgent.entityId;
            agent.EntityClassId = entityClassId;
            agent.CurrentState = Agent.State.Active;
            agent.Health = spawnedAgent.Health;

            Logging.Debug("Agent spawned at {0}, {1}, {2}, entity id {3}", worldPos.x, worldPos.y, worldPos.z, spawnedAgent.entityId);

            return spawnedAgent.entityId;
        }

        static public bool DespawnAgent(Agent agent)
        {
            var world = GameManager.Instance.World;
            if (world == null)
            {
                return false;
            }

            var entity = world.GetEntity(agent.EntityId) as EntityZombie;
            if (entity == null)
            {
                Logging.Out("Entity not found: {0}", agent.EntityId);
                return false;
            }

            // Retain current state.
            agent.Health = entity.Health;

            agent.Velocity = new Vector3(entity.moveDirection.x, entity.moveDirection.z, entity.moveDirection.y);
            agent.Velocity.Validate();

            agent.Position = new Vector3(entity.position.x, entity.position.z, entity.position.y);
            agent.Position.Validate();

            world.RemoveEntity(entity.entityId, EnumRemoveEntityReason.Despawned);

            Logging.Out("Agent despawned, entity id: {0}", agent.EntityId);

            return true;
        }
    }
}
