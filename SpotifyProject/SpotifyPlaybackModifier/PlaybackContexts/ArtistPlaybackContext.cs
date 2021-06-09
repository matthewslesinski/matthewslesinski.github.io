﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpotifyAPI.Web;
using System.Linq;
using SpotifyProject.SpotifyPlaybackModifier.TrackLinking;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;

namespace SpotifyProject.SpotifyPlaybackModifier.PlaybackContexts
{
	public abstract class ArtistPlaybackContext : SpotifyPlaybackQueueBase<SimpleTrackAndAlbumWrapper>, IArtistPlaybackContext
	{
		public ArtistPlaybackContext(SpotifyConfiguration spotifyConfiguration, FullArtist artist) : base(spotifyConfiguration)
		{
			SpotifyContext = artist;
		}

		public FullArtist SpotifyContext { get; }
	}

	public class ExistingArtistPlaybackContext : ArtistPlaybackContext, IOriginalArtistPlaybackContext
	{
		public ExistingArtistPlaybackContext(SpotifyConfiguration spotifyConfiguration, FullArtist artist, ArtistsAlbumsRequest.IncludeGroups albumTypesToInclude) : base(spotifyConfiguration, artist)
		{
			_albumGroupsToInclude = albumTypesToInclude;
		}

		public static async Task<ExistingArtistPlaybackContext> FromSimpleArtist(SpotifyConfiguration spotifyConfiguration, string artistId, ArtistsAlbumsRequest.IncludeGroups albumTypesToInclude)
		{
			var fullArtist = await spotifyConfiguration.Spotify.Artists.Get(artistId);
			return new ExistingArtistPlaybackContext(spotifyConfiguration, fullArtist, albumTypesToInclude);
		}

		private readonly ArtistsAlbumsRequest.IncludeGroups _albumGroupsToInclude;

		public async Task FullyLoad()
		{
			Logger.Information($"Loading albums for artist with Id {SpotifyContext.Id} and Name {SpotifyContext.Name}");
			var includeGroup = _albumGroupsToInclude;
			var artistsAlbumsRequest = new ArtistsAlbumsRequest { IncludeGroupsParam = includeGroup, Limit = 50, Market = _relevantMarket };
			var albumTracksRequest = new AlbumTracksRequest { Limit = 50 };
			var firstAlbumPage = await Spotify.Artists.GetAlbums(SpotifyContext.Id, artistsAlbumsRequest);
			var allAlbums = Spotify.Paginate(firstAlbumPage).Distinct(new SimpleAlbumEqualityComparer()).ToObservable().Finally(() => Logger.Information($"All albums loaded"));
			var allTracks = await allAlbums
				.SelectMany(album => Observable.FromAsync(async () => await Spotify.Albums.GetTracks(album.Id, albumTracksRequest))
					.SelectMany(page => Spotify.Paginate(page).ToObservable())
					.Where(track => track.Artists.Select(artist => artist.Uri).Contains(SpotifyContext.Uri))
					.Select(track => new SimpleTrackAndAlbumWrapper(track, album)))
				.ToAsyncEnumerable().ToListAsync();
			Logger.Information($"All {allTracks.Count()} tracks loaded");
			PlaybackOrder = allTracks;
		}
	}

	public class ReorderedArtistPlaybackContext<OriginalContextT> : ArtistPlaybackContext, IReorderedPlaybackContext<SimpleTrackAndAlbumWrapper, OriginalContextT>
		where OriginalContextT : IArtistPlaybackContext
	{
		public ReorderedArtistPlaybackContext(OriginalContextT baseContext, IEnumerable<SimpleTrackAndAlbumWrapper> reorderedTracks) : base(baseContext.SpotifyConfiguration, baseContext.SpotifyContext)
		{
			PlaybackOrder = reorderedTracks;
			BaseContext = baseContext;
		}

		public OriginalContextT BaseContext { get; }

		public static ReorderedArtistPlaybackContext<OriginalContextT> FromContextAndTracks(OriginalContextT originalContext, IEnumerable<SimpleTrackAndAlbumWrapper> tracks) =>
			new ReorderedArtistPlaybackContext<OriginalContextT>(originalContext, tracks);
	}

	internal class SimpleAlbumEqualityComparer : IEqualityComparer<SimpleAlbum>
	{
		public bool Equals([AllowNull] SimpleAlbum x, [AllowNull] SimpleAlbum y)
		{
			return x?.Name == y?.Name && x?.ReleaseDate == y?.ReleaseDate && x?.AlbumType == y?.AlbumType;
		}

		public int GetHashCode([DisallowNull] SimpleAlbum obj)
		{
			return (obj.Name, obj.ReleaseDate, obj.AlbumType).GetHashCode();
		}
	}
}
