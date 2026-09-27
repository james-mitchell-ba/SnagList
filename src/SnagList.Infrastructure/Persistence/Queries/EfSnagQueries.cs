namespace SnagList.Infrastructure.Persistence.Queries;

using Microsoft.EntityFrameworkCore;
using SnagList.Application.Abstractions;
using SnagList.Application.Common;
using SnagList.Application.Snags.Queries;

public sealed class EfSnagQueries(SnagListDbContext dbContext) : ISnagQueries
{
    public async Task<CursorPage<SnagSummary>> ListAsync(ListSnagsQuery query, CancellationToken ct)
    {
        var cursorKey = OpaqueCursor.Decode<SnagListCursorKey>(query.Cursor);

        var q = dbContext.Snags.AsNoTracking().AsQueryable();
        if (query.LocationId is { } locationId) q = q.Where(s => s.LocationId == locationId);
        if (query.Category is { } category) q = q.Where(s => s.Category == category);
        if (query.Severity is { } severity) q = q.Where(s => s.Severity == severity);
        if (query.Status is { } status) q = q.Where(s => s.Status == status);
        if (cursorKey is not null)
        {
            q = q.Where(s => s.ReportedAt < cursorKey.ReportedAt
                || (s.ReportedAt == cursorKey.ReportedAt && s.Id.CompareTo(cursorKey.Id) < 0));
        }

        var rows = await q.OrderByDescending(s => s.ReportedAt).ThenByDescending(s => s.Id)
            .Take(query.Limit + 1)
            .Select(s => new SnagSummary(
                s.Id, s.LocationId, s.SubLocation, s.Category, s.Severity, s.Status,
                s.ReportedByName, s.ReportedAt, s.Version))
            .ToListAsync(ct);

        var hasMore = rows.Count > query.Limit;
        var page = hasMore ? rows.Take(query.Limit).ToList() : rows;
        var nextCursor = hasMore
            ? OpaqueCursor.Encode(new SnagListCursorKey(page[^1].ReportedAt, page[^1].Id))
            : null;
        return new CursorPage<SnagSummary>(page, nextCursor);
    }

    public async Task<SnagDetail?> GetAsync(Guid id, CancellationToken ct)
    {
        var snag = await dbContext.Snags.AsNoTracking()
            .Include(s => s.Photos)
            .Include(s => s.Comments)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (snag is null) return null;

        return new SnagDetail(
            snag.Id, snag.LocationId, snag.SubLocation, snag.Category, snag.Severity, snag.Description,
            snag.Status, snag.ReportedByStaffId, snag.ReportedByName, snag.ReportedAt, snag.Version,
            snag.Comments
                .Select(c => new SnagCommentDto(c.Id, c.AuthorStaffId, c.AuthorName, c.Body, c.CreatedAt))
                .ToList(),
            snag.Photos
                .Select(p => new SnagPhotoDto(p.BlobKey, p.FileName, p.ContentType, p.SizeBytes, p.UploadedAt))
                .ToList());
    }
}
