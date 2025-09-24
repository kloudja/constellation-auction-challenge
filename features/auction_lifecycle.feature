@auction @lifecycle @us @eu
Feature: Auction lifecycle is mirrored across regions

  Background:
    Given Region US is online
    And   Region EU is online

  Scenario: US creates and activates an auction; EU mirrors it before partition
    When US creates an auction with EndsAtUtc "2025-09-23T12:05:00Z"
    Then US EVENT_STORE has an "AuctionCreated" for that AuctionId
    And  US EVENT_OUTBOX has "Published=false" for that AuctionId

    When US publisher publishes pending outbox
    And  EU drains and applies incoming events
    Then EU has a mirror Auction in "Draft" with the same EndsAtUtc

    When US activates the auction
    Then US EVENT_STORE has an "AuctionActivated" for that AuctionId
    And  US EVENT_OUTBOX has "Published=false" for that AuctionId

    When US publisher publishes pending outbox
    And  EU drains and applies incoming events
    Then EU sees the mirror Auction in "Active"
