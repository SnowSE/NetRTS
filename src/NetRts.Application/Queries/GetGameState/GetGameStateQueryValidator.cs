using FluentValidation;

namespace NetRts.Application.Queries.GetGameState;

/// <summary>
/// Validator for GetGameStateQuery.
/// </summary>
public class GetGameStateQueryValidator : AbstractValidator<GetGameStateQuery>
{
    public GetGameStateQueryValidator()
    {
        RuleFor(x => x.MatchId)
            .NotEmpty()
            .WithMessage("Match ID is required");

        RuleFor(x => x.PlayerId)
            .NotEmpty()
            .WithMessage("Player ID is required");
    }
}
