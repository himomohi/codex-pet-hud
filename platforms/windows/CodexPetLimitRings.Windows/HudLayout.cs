namespace CodexPetLimitRings.Windows;

public sealed record HudPlacement(
    double Scale,
    double PotionWidth,
    double PotionHeight,
    double PrimaryX,
    double SecondaryX,
    double Y);

public static class HudLayout
{
    public static HudPlacement Calculate(PetAnchor anchor, OverlaySettings settings)
    {
        var scale = Math.Clamp(settings.Scale, 0.5, 1.8);
        var potionWidth = 92 * scale;
        var potionHeight = 110 * scale;
        var gap = Math.Clamp(settings.PotionGap, 0, 160) * scale;
        var minimumX = anchor.WorkX;
        var maximumX = Math.Max(minimumX, anchor.WorkRight - potionWidth);
        var desiredLeft = anchor.X - gap - potionWidth + settings.HorizontalOffset;
        var desiredRight = anchor.Right + gap + settings.HorizontalOffset;
        var primaryX = desiredLeft;
        var secondaryX = desiredRight;
        var desiredY = anchor.CenterY - potionHeight / 2 + settings.VerticalOffset;
        var alignment = settings.Alignment?.Trim().ToLowerInvariant() ?? "split";
        var pairWidth = potionWidth * 2 + gap;
        var leftPairFits = desiredLeft - potionWidth - gap >= anchor.WorkX;
        var rightPairFits = desiredRight + pairWidth <= anchor.WorkRight;
        var aboveY = anchor.Y - gap - potionHeight + settings.VerticalOffset;
        var belowY = anchor.Y + anchor.Height + gap + settings.VerticalOffset;
        var aboveFits = aboveY >= anchor.WorkY;
        var belowFits = belowY + potionHeight <= anchor.WorkBottom;

        if (alignment is "left")
        {
            if (leftPairFits || !rightPairFits)
            {
                primaryX = desiredLeft;
                secondaryX = desiredLeft - potionWidth - gap;
            }
            else
            {
                primaryX = desiredRight;
                secondaryX = desiredRight + potionWidth + gap;
            }
        }
        else if (alignment is "right")
        {
            if (rightPairFits || !leftPairFits)
            {
                primaryX = desiredRight;
                secondaryX = desiredRight + potionWidth + gap;
            }
            else
            {
                primaryX = desiredLeft;
                secondaryX = desiredLeft - potionWidth - gap;
            }
        }
        else if (alignment is "above" or "below")
        {
            var pairX = anchor.X + anchor.Width / 2 - pairWidth / 2 + settings.HorizontalOffset;
            if (pairWidth <= anchor.WorkWidth)
            {
                pairX = Math.Clamp(pairX, anchor.WorkX, anchor.WorkRight - pairWidth);
                primaryX = pairX;
                secondaryX = pairX + potionWidth + gap;
            }
            else
            {
                primaryX = minimumX;
                secondaryX = maximumX;
            }
            desiredY = alignment is "above"
                ? aboveFits || !belowFits ? aboveY : belowY
                : belowFits || !aboveFits ? belowY : aboveY;
        }

        if (alignment is "split")
        {
            var leftFits = desiredLeft >= anchor.WorkX;
            var rightFits = desiredRight + potionWidth <= anchor.WorkRight;
            if (!leftFits || !rightFits)
            {
                if (desiredRight + pairWidth <= anchor.WorkRight)
                {
                    primaryX = desiredRight;
                    secondaryX = desiredRight + potionWidth + gap;
                }
                else if (desiredLeft - potionWidth - gap >= anchor.WorkX)
                {
                    primaryX = desiredLeft;
                    secondaryX = desiredLeft - potionWidth - gap;
                }
                else
                {
                    primaryX = Math.Clamp(desiredLeft, minimumX, maximumX);
                    secondaryX = Math.Clamp(desiredRight, minimumX, maximumX);
                }
            }
        }

        if (alignment is "left" or "right")
        {
            if (pairWidth <= anchor.WorkWidth)
            {
                var pairStart = Math.Min(primaryX, secondaryX);
                var clampedStart = Math.Clamp(pairStart, anchor.WorkX, anchor.WorkRight - pairWidth);
                var shift = clampedStart - pairStart;
                primaryX += shift;
                secondaryX += shift;
            }
            else
            {
                primaryX = Math.Clamp(primaryX, minimumX, maximumX);
                secondaryX = Math.Clamp(secondaryX, minimumX, maximumX);
            }
        }

        var maximumY = Math.Max(anchor.WorkY, anchor.WorkBottom - potionHeight);
        var y = Math.Clamp(desiredY, anchor.WorkY, maximumY);
        return new HudPlacement(scale, potionWidth, potionHeight, primaryX, secondaryX, y);
    }
}
