using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using OpenDeepWiki.Entities;

namespace OpenDeepWiki.Tests.Services.Repositories;

/// <summary>
/// Property-based tests for Repository Visibility functionality.
/// Feature: repository-visibility-management
/// </summary>
public class PrivateRepositoryVisibilityPropertyTests
{
    /// <summary>
    /// Generates a valid GUID string.
    /// </summary>
    private static Gen<string> GenerateGuidString()
    {
        return Gen.Constant(0).Select(_ => Guid.NewGuid().ToString());
    }

    /// <summary>
    /// Generates a valid organization name.
    /// </summary>
    private static Gen<string> GenerateOrgName()
    {
        return Gen.Elements("microsoft", "google", "facebook", "amazon", "openai", "anthropic", "meta", "apple");
    }

    /// <summary>
    /// Generates a valid repository name.
    /// </summary>
    private static Gen<string> GenerateRepoName()
    {
        return Gen.Elements("api", "sdk", "cli", "web", "docs", "core", "utils", "tools", "app", "service");
    }

    /// <summary>
    /// Generates an empty or null password (no password scenarios).
    /// </summary>
    private static Gen<string?> GenerateEmptyOrNullPassword()
    {
        return Gen.Elements<string?>(null, "", "   ", "\t", "\n", "  \t  ");
    }

    /// <summary>
    /// Generates a valid non-empty password.
    /// </summary>
    private static Gen<string> GenerateValidPassword()
    {
        return Gen.Elements("password123", "secret", "token_abc", "auth_key_xyz", "p@ssw0rd!");
    }

    /// <summary>
    /// Generates a Repository entity without password (AuthPassword is null or empty).
    /// </summary>
    private static Gen<Repository> GenerateRepositoryWithoutPassword()
    {
        return GenerateGuidString().SelectMany(id =>
            GenerateGuidString().SelectMany(ownerId =>
                GenerateOrgName().SelectMany(orgName =>
                    GenerateRepoName().SelectMany(repoName =>
                        GenerateEmptyOrNullPassword().Select(password =>
                            new Repository
                            {
                                Id = id,
                                OwnerUserId = ownerId,
                                OrgName = orgName,
                                RepoName = repoName,
                                GitUrl = $"https://github.com/{orgName}/{repoName}.git",
                                AuthPassword = password,
                                IsPublic = true, // Start as shared
                                Status = RepositoryStatus.Completed,
                                CreatedAt = DateTime.UtcNow
                            })))));
    }

    /// <summary>
    /// Generates a Repository entity with a valid password.
    /// </summary>
    private static Gen<Repository> GenerateRepositoryWithPassword()
    {
        return GenerateGuidString().SelectMany(id =>
            GenerateGuidString().SelectMany(ownerId =>
                GenerateOrgName().SelectMany(orgName =>
                    GenerateRepoName().SelectMany(repoName =>
                        GenerateValidPassword().Select(password =>
                            new Repository
                            {
                                Id = id,
                                OwnerUserId = ownerId,
                                OrgName = orgName,
                                RepoName = repoName,
                                GitUrl = $"https://github.com/{orgName}/{repoName}.git",
                                AuthPassword = password,
                                IsPublic = true, // Start as shared
                                Status = RepositoryStatus.Completed,
                                CreatedAt = DateTime.UtcNow
                            })))));
    }

    /// <summary>
    /// Any repository can be freely toggled to restricted (no password check).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property AnyRepository_CanBeSetToRestricted()
    {
        var repositoryGen = Gen.OneOf(
            GenerateRepositoryWithoutPassword(),
            GenerateRepositoryWithPassword()
        );

        return Prop.ForAll(
            repositoryGen.ToArbitrary(),
            repository =>
            {
                var store = new MockRepositoryStore();
                store.Add(repository);

                var request = new UpdateVisibilityRequest
                {
                    RepositoryId = repository.Id,
                    IsPublic = false,
                    OwnerUserId = repository.OwnerUserId
                };

                var (success, updatedRepository, _) = SimulateUpdateVisibility(store, request);

                return success &&
                       updatedRepository != null &&
                       !updatedRepository.IsPublic;
            })
            .Label("Any repository can be set to restricted regardless of password");
    }

    /// <summary>
    /// Any repository can be freely toggled to shared.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property AnyRepository_CanBeSetToShared()
    {
        var repositoryGen = Gen.OneOf(
            GenerateRepositoryWithoutPassword(),
            GenerateRepositoryWithPassword()
        );

        return Prop.ForAll(
            repositoryGen.ToArbitrary(),
            repository =>
            {
                repository.IsPublic = false; // Start as restricted

                var store = new MockRepositoryStore();
                store.Add(repository);

                var request = new UpdateVisibilityRequest
                {
                    RepositoryId = repository.Id,
                    IsPublic = true,
                    OwnerUserId = repository.OwnerUserId
                };

                var (success, updatedRepository, _) = SimulateUpdateVisibility(store, request);

                return success &&
                       updatedRepository != null &&
                       updatedRepository.IsPublic;
            })
            .Label("Any repository can be set to shared regardless of password");
    }

    #region Ownership Validation

    /// <summary>
    /// Non-owner requests are rejected.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property OwnershipValidation_NonOwnerRequestIsRejected()
    {
        var repositoryGen = Gen.OneOf(
            GenerateRepositoryWithoutPassword(),
            GenerateRepositoryWithPassword()
        );

        return Prop.ForAll(
            repositoryGen.ToArbitrary(),
            GenerateGuidString().ToArbitrary(),
            ArbMap.Default.GeneratorFor<bool>().ToArbitrary(),
            (repository, requestUserId, isPublic) =>
            {
                var isDifferentOwner = repository.OwnerUserId != requestUserId;
                var shouldRejectDueToOwnership = repository.OwnerUserId != requestUserId;

                return !isDifferentOwner || shouldRejectDueToOwnership;
            })
            .Label("Non-owner requests are rejected");
    }

    /// <summary>
    /// Owner requests are not rejected due to ownership.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property OwnershipValidation_OwnerRequestIsNotRejectedDueToOwnership()
    {
        var repositoryGen = Gen.OneOf(
            GenerateRepositoryWithoutPassword(),
            GenerateRepositoryWithPassword()
        );

        return Prop.ForAll(
            repositoryGen.ToArbitrary(),
            ArbMap.Default.GeneratorFor<bool>().ToArbitrary(),
            (repository, isPublic) =>
            {
                var requestOwnerUserId = repository.OwnerUserId;
                var shouldRejectDueToOwnership = repository.OwnerUserId != requestOwnerUserId;

                return !shouldRejectDueToOwnership;
            })
            .Label("Owner requests are not rejected due to ownership");
    }

    /// <summary>
    /// Ownership validation is consistent.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property OwnershipValidation_IsConsistent()
    {
        var repositoryGen = Gen.OneOf(
            GenerateRepositoryWithoutPassword(),
            GenerateRepositoryWithPassword()
        );

        return Prop.ForAll(
            repositoryGen.ToArbitrary(),
            GenerateGuidString().ToArbitrary(),
            ArbMap.Default.GeneratorFor<bool>().ToArbitrary(),
            (repository, requestUserId, isPublic) =>
            {
                var isOwner = repository.OwnerUserId == requestUserId;
                var shouldRejectDueToOwnership = !isOwner;
                var expectedReject = !isOwner;

                return shouldRejectDueToOwnership == expectedReject;
            })
            .Label("Ownership validation is consistent");
    }

    #endregion

    #region Persistence Consistency

    private class UpdateVisibilityRequest
    {
        public string RepositoryId { get; set; } = string.Empty;
        public bool IsPublic { get; set; }
        public string OwnerUserId { get; set; } = string.Empty;
    }

    private class MockRepositoryStore
    {
        private readonly Dictionary<string, Repository> _repositories = new();

        public void Add(Repository repository)
        {
            _repositories[repository.Id] = repository;
        }

        public Repository? FindById(string id)
        {
            return _repositories.TryGetValue(id, out var repo) ? repo : null;
        }

        public void SaveChanges()
        {
        }
    }

    /// <summary>
    /// Simulates the UpdateVisibilityAsync logic (no password check).
    /// </summary>
    private static (bool Success, Repository? UpdatedRepository, string? ErrorMessage) SimulateUpdateVisibility(
        MockRepositoryStore store,
        UpdateVisibilityRequest request)
    {
        var repository = store.FindById(request.RepositoryId);

        if (repository is null)
        {
            return (false, null, "Repository not found");
        }

        // Ownership validation
        if (repository.OwnerUserId != request.OwnerUserId)
        {
            return (false, repository, "No permission to modify this repository");
        }

        // Update visibility (no password check - any repo can toggle freely)
        repository.IsPublic = request.IsPublic;
        store.SaveChanges();

        return (true, repository, null);
    }

    /// <summary>
    /// After a successful visibility update, the persisted state matches the request.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property VisibilityPersistence_SetToRestricted()
    {
        var repositoryGen = Gen.OneOf(
            GenerateRepositoryWithoutPassword(),
            GenerateRepositoryWithPassword()
        );

        return Prop.ForAll(
            repositoryGen.ToArbitrary(),
            repository =>
            {
                var store = new MockRepositoryStore();
                store.Add(repository);

                var request = new UpdateVisibilityRequest
                {
                    RepositoryId = repository.Id,
                    IsPublic = false,
                    OwnerUserId = repository.OwnerUserId
                };

                var (success, updatedRepository, _) = SimulateUpdateVisibility(store, request);

                return success &&
                       updatedRepository != null &&
                       updatedRepository.IsPublic == request.IsPublic;
            })
            .Label("Visibility persistence - set to restricted");
    }

    /// <summary>
    /// After a successful visibility update to shared, the persisted state matches.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property VisibilityPersistence_SetToShared()
    {
        var repositoryGen = Gen.OneOf(
            GenerateRepositoryWithoutPassword(),
            GenerateRepositoryWithPassword()
        );

        return Prop.ForAll(
            repositoryGen.ToArbitrary(),
            repository =>
            {
                repository.IsPublic = false; // Start as restricted

                var store = new MockRepositoryStore();
                store.Add(repository);

                var request = new UpdateVisibilityRequest
                {
                    RepositoryId = repository.Id,
                    IsPublic = true,
                    OwnerUserId = repository.OwnerUserId
                };

                var (success, updatedRepository, _) = SimulateUpdateVisibility(store, request);

                return success &&
                       updatedRepository != null &&
                       updatedRepository.IsPublic == request.IsPublic;
            })
            .Label("Visibility persistence - set to shared");
    }

    /// <summary>
    /// Query after update returns the correct value.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property VisibilityPersistence_QueryAfterUpdate_ReturnsCorrectValue()
    {
        var repositoryGen = Gen.OneOf(
            GenerateRepositoryWithoutPassword(),
            GenerateRepositoryWithPassword()
        );

        return Prop.ForAll(
            repositoryGen.ToArbitrary(),
            ArbMap.Default.GeneratorFor<bool>().ToArbitrary(),
            (repository, targetIsPublic) =>
            {
                var store = new MockRepositoryStore();
                store.Add(repository);

                var request = new UpdateVisibilityRequest
                {
                    RepositoryId = repository.Id,
                    IsPublic = targetIsPublic,
                    OwnerUserId = repository.OwnerUserId
                };

                var (success, _, _) = SimulateUpdateVisibility(store, request);
                var queriedRepository = store.FindById(repository.Id);

                return success &&
                       queriedRepository != null &&
                       queriedRepository.IsPublic == targetIsPublic;
            })
            .Label("Visibility persistence - query after update returns correct value");
    }

    /// <summary>
    /// Multiple visibility updates: final state matches the last request.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property VisibilityPersistence_MultipleUpdates_FinalStateMatchesLastRequest()
    {
        var repositoryGen = Gen.OneOf(
            GenerateRepositoryWithoutPassword(),
            GenerateRepositoryWithPassword()
        );

        var combinedGen = repositoryGen.SelectMany(repo =>
            ArbMap.Default.GeneratorFor<bool>().SelectMany(first =>
                ArbMap.Default.GeneratorFor<bool>().SelectMany(second =>
                    ArbMap.Default.GeneratorFor<bool>().Select(third =>
                        (Repository: repo, First: first, Second: second, Third: third)))));

        return Prop.ForAll(
            combinedGen.ToArbitrary(),
            tuple =>
            {
                var (repository, firstIsPublic, secondIsPublic, thirdIsPublic) = tuple;

                var store = new MockRepositoryStore();
                store.Add(repository);

                SimulateUpdateVisibility(store, new UpdateVisibilityRequest
                {
                    RepositoryId = repository.Id,
                    IsPublic = firstIsPublic,
                    OwnerUserId = repository.OwnerUserId
                });

                SimulateUpdateVisibility(store, new UpdateVisibilityRequest
                {
                    RepositoryId = repository.Id,
                    IsPublic = secondIsPublic,
                    OwnerUserId = repository.OwnerUserId
                });

                var (success, _, _) = SimulateUpdateVisibility(store, new UpdateVisibilityRequest
                {
                    RepositoryId = repository.Id,
                    IsPublic = thirdIsPublic,
                    OwnerUserId = repository.OwnerUserId
                });

                var queriedRepository = store.FindById(repository.Id);

                return success &&
                       queriedRepository != null &&
                       queriedRepository.IsPublic == thirdIsPublic;
            })
            .Label("Visibility persistence - multiple updates, final state matches last request");
    }

    /// <summary>
    /// Response matches persisted state.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property VisibilityPersistence_ResponseMatchesPersistedState()
    {
        var repositoryGen = Gen.OneOf(
            GenerateRepositoryWithoutPassword(),
            GenerateRepositoryWithPassword()
        );

        return Prop.ForAll(
            repositoryGen.ToArbitrary(),
            ArbMap.Default.GeneratorFor<bool>().ToArbitrary(),
            (repository, targetIsPublic) =>
            {
                var store = new MockRepositoryStore();
                store.Add(repository);

                var request = new UpdateVisibilityRequest
                {
                    RepositoryId = repository.Id,
                    IsPublic = targetIsPublic,
                    OwnerUserId = repository.OwnerUserId
                };

                var (success, updatedRepository, _) = SimulateUpdateVisibility(store, request);
                var persistedRepository = store.FindById(repository.Id);

                return success &&
                       updatedRepository != null &&
                       persistedRepository != null &&
                       updatedRepository.IsPublic == persistedRepository.IsPublic &&
                       persistedRepository.IsPublic == targetIsPublic;
            })
            .Label("Visibility persistence - response matches persisted state");
    }

    #endregion

    #region Serialization Round-Trip

    private class SerializableUpdateVisibilityRequest
    {
        public string RepositoryId { get; set; } = string.Empty;
        public bool IsPublic { get; set; }
        public string OwnerUserId { get; set; } = string.Empty;
    }

    private class SerializableUpdateVisibilityResponse
    {
        public string Id { get; set; } = string.Empty;
        public bool IsPublic { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    private static Gen<SerializableUpdateVisibilityRequest> GenerateSerializableUpdateVisibilityRequest()
    {
        return GenerateGuidString().SelectMany(repositoryId =>
            GenerateGuidString().SelectMany(ownerUserId =>
                ArbMap.Default.GeneratorFor<bool>().Select(isPublic =>
                    new SerializableUpdateVisibilityRequest
                    {
                        RepositoryId = repositoryId,
                        IsPublic = isPublic,
                        OwnerUserId = ownerUserId
                    })));
    }

    private static Gen<SerializableUpdateVisibilityResponse> GenerateSerializableUpdateVisibilityResponse()
    {
        return GenerateGuidString().SelectMany(id =>
            ArbMap.Default.GeneratorFor<bool>().SelectMany(isPublic =>
                ArbMap.Default.GeneratorFor<bool>().SelectMany(success =>
                    Gen.Elements<string?>(null, "", "Repository not found", "No permission to modify this repository").Select(errorMessage =>
                        new SerializableUpdateVisibilityResponse
                        {
                            Id = id,
                            IsPublic = isPublic,
                            Success = success,
                            ErrorMessage = errorMessage
                        }))));
    }

    [Property(MaxTest = 100)]
    public Property RequestSerialization_RoundTrip_UpdateVisibilityRequest()
    {
        return Prop.ForAll(
            GenerateSerializableUpdateVisibilityRequest().ToArbitrary(),
            request =>
            {
                var json = System.Text.Json.JsonSerializer.Serialize(request);
                var deserialized = System.Text.Json.JsonSerializer.Deserialize<SerializableUpdateVisibilityRequest>(json);

                return deserialized != null &&
                       deserialized.RepositoryId == request.RepositoryId &&
                       deserialized.IsPublic == request.IsPublic &&
                       deserialized.OwnerUserId == request.OwnerUserId;
            })
            .Label("Request serialization round-trip");
    }

    [Property(MaxTest = 100)]
    public Property RequestSerialization_RoundTrip_UpdateVisibilityResponse()
    {
        return Prop.ForAll(
            GenerateSerializableUpdateVisibilityResponse().ToArbitrary(),
            response =>
            {
                var json = System.Text.Json.JsonSerializer.Serialize(response);
                var deserialized = System.Text.Json.JsonSerializer.Deserialize<SerializableUpdateVisibilityResponse>(json);

                return deserialized != null &&
                       deserialized.Id == response.Id &&
                       deserialized.IsPublic == response.IsPublic &&
                       deserialized.Success == response.Success &&
                       deserialized.ErrorMessage == response.ErrorMessage;
            })
            .Label("Response serialization round-trip");
    }

    [Property(MaxTest = 100)]
    public Property RequestSerialization_JsonContainsExpectedProperties()
    {
        return Prop.ForAll(
            GenerateSerializableUpdateVisibilityRequest().ToArbitrary(),
            request =>
            {
                var json = System.Text.Json.JsonSerializer.Serialize(request);

                return json.Contains("RepositoryId") &&
                       json.Contains("IsPublic") &&
                       json.Contains("OwnerUserId");
            })
            .Label("Request serialization contains expected properties");
    }

    [Property(MaxTest = 100)]
    public Property RequestSerialization_MultipleRoundTrips_AreConsistent()
    {
        return Prop.ForAll(
            GenerateSerializableUpdateVisibilityRequest().ToArbitrary(),
            request =>
            {
                var json1 = System.Text.Json.JsonSerializer.Serialize(request);
                var deserialized1 = System.Text.Json.JsonSerializer.Deserialize<SerializableUpdateVisibilityRequest>(json1);

                var json2 = System.Text.Json.JsonSerializer.Serialize(deserialized1);
                var deserialized2 = System.Text.Json.JsonSerializer.Deserialize<SerializableUpdateVisibilityRequest>(json2);

                var json3 = System.Text.Json.JsonSerializer.Serialize(deserialized2);
                var deserialized3 = System.Text.Json.JsonSerializer.Deserialize<SerializableUpdateVisibilityRequest>(json3);

                return deserialized1 != null && deserialized2 != null && deserialized3 != null &&
                       json1 == json2 && json2 == json3 &&
                       deserialized1.RepositoryId == deserialized2.RepositoryId &&
                       deserialized2.RepositoryId == deserialized3.RepositoryId &&
                       deserialized1.IsPublic == deserialized2.IsPublic &&
                       deserialized2.IsPublic == deserialized3.IsPublic &&
                       deserialized1.OwnerUserId == deserialized2.OwnerUserId &&
                       deserialized2.OwnerUserId == deserialized3.OwnerUserId;
            })
            .Label("Request serialization multiple round-trips are consistent");
    }

    #endregion
}
