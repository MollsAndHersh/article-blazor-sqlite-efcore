﻿using Blazor.Sqlite.Client.Data;
using Blazor.Sqlite.Client.Features.Conferences.Models;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;

namespace Blazor.Sqlite.Client.Features.Conferences.Services
{
    public class ContributionsService
    {
        private readonly DatabaseContext _dbContext;
        private readonly HttpClient _httpClient;

        private bool _hasSynced = false;
        public ContributionsService(DatabaseContext dbContext, HttpClient httpClient)
        {
            _dbContext = dbContext;
            _httpClient = httpClient;
        }

        public async Task InitAsync()
        {
            if (_hasSynced) return;

            if (_dbContext.Contributions.Count() > 0) return;

            await SyncSpeakers();

            var result = await _httpClient.GetFromJsonAsync<Root<ContributionDto>>("/sample-data/contributions.json");
            if (result?.Items.Count > 0)
            {
                var index = 1;
                result.Items.ForEach(item =>
                {
                    item.Id = index++;
                    _dbContext.Contributions.Add(item);
                    if (item.Speaker.Any())
                    {
                        item.Speaker.ForEach(speakerId =>
                        {
                            _dbContext.ContributionSpeakers.Add(new ContributionSpeaker
                            {
                                ContributionId = item.Id,
                                SpeakerId = speakerId
                            });
                        });
                    }
                });
            }

            await _dbContext.SaveChangesAsync();
            _hasSynced = true;
        }

        public Task<List<Contribution>> GetContributions(int skip = 0, int take = Int32.MaxValue, CancellationToken cancellationToken = default)
        {
            return _dbContext.Contributions.Include(c => c.ContributionSpeakers).ThenInclude(cs => cs.Speaker).Skip(skip).Take(take).ToListAsync(cancellationToken);
        }

        public Task<int> GetContributionCount(CancellationToken cancellationToken = default)
        {
            return _dbContext.Contributions.CountAsync(cancellationToken);
        }

        public async Task<bool> SaveContributionAsync(Contribution contribution, List<int> speakerIds)
        {
            try
            {
                if (contribution.Id != 0)
                {
                    var currentContribution = await _dbContext.Contributions.Include(c => c.ContributionSpeakers).FirstOrDefaultAsync(c => c.Id == contribution.Id);
                    if (currentContribution == null)
                    {
                        return false;
                    }
                    currentContribution.Title = contribution.Title;
                    currentContribution.Abstract = contribution.Abstract;
                    currentContribution.Type = contribution.Type;
                    currentContribution.PrimaryTag = contribution.PrimaryTag;
                    currentContribution.ExternalSpeaker = contribution.ExternalSpeaker;
                    currentContribution.Date = contribution.Date;
                    foreach (var speakerId in speakerIds)
                    {
                        if (!_dbContext.ContributionSpeakers.Any(cs => cs.ContributionId == currentContribution.Id && cs.SpeakerId == speakerId))
                        {
                            _dbContext.ContributionSpeakers.Add(new ContributionSpeaker
                            {
                                SpeakerId = speakerId,
                                ContributionId = currentContribution.Id
                            });
                        }

                        var diff = currentContribution.ContributionSpeakers.Select(cs => cs.SpeakerId).Except(speakerIds);
                        foreach (var removeItem in diff)
                        {
                            var relation = await _dbContext.ContributionSpeakers.FirstOrDefaultAsync(cs => cs.ContributionId == currentContribution.Id && cs.SpeakerId == removeItem);
                            if (relation != null)
                            {
                                _dbContext.ContributionSpeakers.Remove(relation);
                            }
                        }
                    }
                }
                else
                {
                    var entry = _dbContext.Contributions.Add(contribution);
                    foreach (var speakerId in speakerIds)
                    {
                        _dbContext.ContributionSpeakers.Add(new ContributionSpeaker
                        {
                            SpeakerId = speakerId,
                            ContributionId = entry.Entity.Id
                        });
                    }
                }
                await _dbContext.SaveChangesAsync();
                return true;
            }
            catch(Exception e)
            {
                Console.WriteLine(e.ToString());
            }
            return false;
        }
         
        public async Task<bool> DeleteContributionAsync(int id)
        {
            try
            {
                var currentContribution = await _dbContext.Contributions.FirstOrDefaultAsync(c => c.Id == id);
                if (currentContribution != null)
                {
                    _dbContext.Contributions.Remove(currentContribution);
                    await _dbContext.SaveChangesAsync();
                    return true;
                }
            }
            catch(Exception e)
            {
                Console.WriteLine(e.Message);
            }
            return false;
        }

        public Task<List<Speaker>> GetSpeakersAsync()
        {
            return _dbContext.Speakers.ToListAsync();
        }

        private async Task SyncSpeakers()
        {
            var result = await _httpClient.GetFromJsonAsync<Root<Speaker>>("/sample-data/speakers.json");
            if (result != null)
            {
                await _dbContext.Speakers.AddRangeAsync(result.Items);
            }

        }
    }
}
