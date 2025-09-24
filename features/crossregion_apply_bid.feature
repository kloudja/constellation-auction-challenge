@crossregion @applied @idempotency
Feature: Cross-region apply is at-least-once and idempotent

  Scenario: EU applies a US BidPlaced once
    Given US EVENT_STORE contains EventId E for "BidPlaced" (AuctionId=A)
    And   EU APPLIED_EVENT does not contain E
    When  EU drains and applies
    Then  EU inserts the bid into its BID table (preserving SourceRegionId and CreatedAtUtc)
    And   EU APPLIED_EVENT contains E with AppliedAtUtc set
    And   applying E again makes no changes
