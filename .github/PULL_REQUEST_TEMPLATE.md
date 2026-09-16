<!-- Keep this template short; CI runs the pr gate and requirement trace automatically. -->

## Project and Requirements

* Project slug: `{{project}}`
* Requirements addressed: REQ-
* Work items: AB#  /  #

## Evidence

* Plan: .copilot-tracking/plans/{{date}}/{{slug}}-plan.md
* Change record: .copilot-tracking/changes/{{date}}/{{slug}}-changes.md
* Test evidence: .copilot-tracking/sdlc/{{project}}/evidence/tests-{{date}}.md
* Code review: .copilot-tracking/reviews/code-reviews/{{branch}}/review.md

## Checklist

* [ ] Every requirement above is cited in tests and source
* [ ] No secrets, customer identifiers, or personal data in the diff or artifacts
* [ ] Infrastructure changes reviewed with `what-if` output attached
* [ ] Human reviewer has read the AI-generated review, not only its verdict
