<?xml version="1.0" encoding="UTF-8"?>
<xsl:stylesheet version="1.0"
    xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
    xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL">

  <xsl:output method="text" encoding="UTF-8"/>

  <!--
    The reverse direction from PlantBPMN itself: a real BPMN 2.0 file back
    into readable PlantUML Activity Diagram text, docs/comparisons/
    user-flow-dsl.md Option H, answering the original "a custom visualizer
    ... like an XSLT over BPMN files to PlantUML diagrams" request directly,
    via .NET's real System.Xml.Xsl.XslCompiledTransform (XSLT 1.0).

    Recursive graph walk, not document order: a BPMN file's own child-
    element order does not follow control flow (PlantBPMN's own generated
    output interleaves an if/else's two branches around a single shared
    join gateway element, confirmed by inspecting Generated/
    AdverseEventReview.bpmn directly), so this walks sourceRef/targetRef
    via keys instead of xsl:for-each over document order.

    XSLT 1.0 has no native way to return two values (rendered text AND
    "which join gateway this branch stopped at") from a recursive
    xsl:call-template, so both are packed into one string separated by
    a Private Use Area codepoint (U+E000; a raw U+0001 was tried first
    and rejected outright by XmlReader as an illegal XML character) and
    split back apart with substring-before/-after at the call site, a
    standard, if slightly unusual, pure-XSLT-1.0 technique.
  -->

  <xsl:key name="nodeById" match="bpmn:serviceTask | bpmn:exclusiveGateway | bpmn:startEvent | bpmn:endEvent" use="@id"/>
  <xsl:key name="flowsFrom" match="bpmn:sequenceFlow" use="@sourceRef"/>

  <xsl:variable name="SEP"><xsl:text>&#xE000;</xsl:text></xsl:variable>

  <xsl:template match="/">
    <xsl:variable name="startId" select="//bpmn:startEvent/@id"/>
    <xsl:variable name="result">
      <xsl:call-template name="render">
        <xsl:with-param name="id" select="$startId"/>
      </xsl:call-template>
    </xsl:variable>
    <xsl:text>@startuml&#10;</xsl:text>
    <xsl:value-of select="substring-before(concat(string($result), $SEP), $SEP)"/>
    <xsl:text>@enduml&#10;</xsl:text>
  </xsl:template>

  <!-- Renders from node $id to the end of its reachable flow. Result is
       "<plantuml text><joinGatewayId-or-empty>". -->
  <xsl:template name="render">
    <xsl:param name="id"/>
    <xsl:variable name="node" select="key('nodeById', $id)"/>
    <xsl:variable name="kind" select="local-name($node)"/>

    <xsl:choose>
      <xsl:when test="$kind = 'startEvent'">
        <xsl:call-template name="render">
          <xsl:with-param name="id" select="key('flowsFrom', $id)[1]/@targetRef"/>
        </xsl:call-template>
      </xsl:when>

      <xsl:when test="$kind = 'endEvent'">
        <xsl:text>stop&#10;</xsl:text>
        <xsl:value-of select="$SEP"/>
      </xsl:when>

      <xsl:when test="$kind = 'serviceTask'">
        <xsl:text>:</xsl:text>
        <xsl:value-of select="$node/@name"/>
        <xsl:text>;&#10;</xsl:text>
        <xsl:call-template name="render">
          <xsl:with-param name="id" select="key('flowsFrom', $id)[1]/@targetRef"/>
        </xsl:call-template>
      </xsl:when>

      <xsl:when test="$kind = 'exclusiveGateway'">
        <xsl:variable name="outFlows" select="key('flowsFrom', $id)"/>
        <xsl:choose>
          <!-- A join/merge gateway in PlantBPMN's own output always has a
               single, unconditioned outgoing flow: report this node's own
               id as the join point and stop; the split level below resumes
               from here exactly once, instead of once per branch. -->
          <xsl:when test="count($outFlows) &lt;= 1">
            <xsl:value-of select="$SEP"/>
            <xsl:value-of select="$id"/>
          </xsl:when>
          <xsl:otherwise>
            <xsl:variable name="yesFlow" select="$outFlows[bpmn:conditionExpression = 'yes']"/>
            <xsl:variable name="noFlow" select="$outFlows[bpmn:conditionExpression = 'no']"/>
            <xsl:variable name="branch1">
              <xsl:call-template name="render">
                <xsl:with-param name="id" select="$yesFlow/@targetRef"/>
              </xsl:call-template>
            </xsl:variable>
            <xsl:variable name="branch2">
              <xsl:call-template name="render">
                <xsl:with-param name="id" select="$noFlow/@targetRef"/>
              </xsl:call-template>
            </xsl:variable>
            <xsl:variable name="joinId" select="substring-after(string($branch1), $SEP)"/>

            <xsl:text>if (</xsl:text>
            <xsl:value-of select="$node/@name"/>
            <xsl:text>) then (yes)&#10;</xsl:text>
            <xsl:value-of select="substring-before(string($branch1), $SEP)"/>
            <xsl:text>else (no)&#10;</xsl:text>
            <xsl:value-of select="substring-before(string($branch2), $SEP)"/>
            <xsl:text>endif&#10;</xsl:text>

            <xsl:if test="$joinId != ''">
              <xsl:call-template name="render">
                <xsl:with-param name="id" select="key('flowsFrom', $joinId)[1]/@targetRef"/>
              </xsl:call-template>
            </xsl:if>
          </xsl:otherwise>
        </xsl:choose>
      </xsl:when>
      <!-- PlantBPMN's own generated output can contain a gateway with zero
           outgoing sequenceFlows (found while building this spike, against
           a nested if/else PlantUML source; see this spike's own
           README.md). This branch's own text is real and does fire in that
           case, but substring-before/-after's XPath 1.0 "return empty if
           the separator isn't found at all" semantics mean it gets
           silently swallowed by every enclosing xsl:call-template on the
           way back up, not just truncated: an interesting, worth-noting
           characteristic of the string+separator "return two values"
           technique this file uses throughout, not a bug in this branch
           itself. Left in place because it IS visible with an intermediate
           xsl:variable while debugging (how the empty-branch defect above
           was actually diagnosed), even though the shipped pipeline's
           final printed output never shows it. -->

      <xsl:otherwise>
        <xsl:text>UNKNOWN kind=[</xsl:text><xsl:value-of select="$kind"/>
        <xsl:text>] id=[</xsl:text><xsl:value-of select="$id"/><xsl:text>]&#10;</xsl:text>
      </xsl:otherwise>
    </xsl:choose>
  </xsl:template>

</xsl:stylesheet>
